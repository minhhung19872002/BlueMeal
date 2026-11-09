namespace BlueMeal.Infrastructure.Persistence.Migrations;

/// <summary>Raw SQL for objects EF cannot model: partitioned audit_log and its partition maintenance.</summary>
internal static class AuditLogSql
{
    public const string Create = """
        CREATE TABLE IF NOT EXISTS app.audit_log (
            id uuid NOT NULL,
            at timestamptz NOT NULL,
            user_id uuid NULL,
            user_name text NULL,
            ip text NULL,
            user_agent varchar(500) NULL,
            action integer NOT NULL,
            entity_type text NULL,
            entity_id text NULL,
            old_values jsonb NULL,
            new_values jsonb NULL,
            success boolean NOT NULL DEFAULT true,
            message varchar(2000) NULL,
            trace_id text NULL,
            CONSTRAINT pk_audit_log PRIMARY KEY (id, at)
        ) PARTITION BY RANGE (at);

        CREATE INDEX IF NOT EXISTS ix_audit_log_at ON app.audit_log (at DESC);
        CREATE INDEX IF NOT EXISTS ix_audit_log_user ON app.audit_log (user_id, at DESC);
        CREATE INDEX IF NOT EXISTS ix_audit_log_entity ON app.audit_log (entity_type, entity_id);
        CREATE INDEX IF NOT EXISTS ix_audit_log_action ON app.audit_log (action, at DESC);

        CREATE TABLE IF NOT EXISTS app.audit_log_default PARTITION OF app.audit_log DEFAULT;

        -- Creates monthly partitions from the current month up to N months ahead (called at startup and by the housekeeping job).
        CREATE OR REPLACE FUNCTION app.ensure_audit_partitions(months_ahead int) RETURNS void
        LANGUAGE plpgsql SECURITY DEFINER SET search_path = app, pg_temp AS $$
        DECLARE
            m date := date_trunc('month', now())::date;
            i int;
            part text;
        BEGIN
            FOR i IN 0..months_ahead LOOP
                part := 'audit_log_' || to_char(m + (i || ' month')::interval, 'YYYYMM');
                IF to_regclass('app.' || part) IS NULL THEN
                    EXECUTE format('CREATE TABLE app.%I PARTITION OF app.audit_log FOR VALUES FROM (%L) TO (%L)',
                        part, m + (i || ' month')::interval, m + ((i + 1) || ' month')::interval);
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bluemeal_app') THEN
                        EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON app.%I FROM bluemeal_app', part);
                        EXECUTE format('GRANT SELECT, INSERT ON app.%I TO bluemeal_app', part);
                    END IF;
                END IF;
            END LOOP;
        END $$;

        -- Drops partitions older than the retention period (months). Run by the DB owner only.
        CREATE OR REPLACE FUNCTION app.drop_old_audit_partitions(keep_months int) RETURNS int
        LANGUAGE plpgsql AS $$
        DECLARE
            r record; cutoff text := to_char(date_trunc('month', now()) - (keep_months || ' month')::interval, 'YYYYMM'); n int := 0;
        BEGIN
            FOR r IN SELECT c.relname FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid
                     WHERE i.inhparent = 'app.audit_log'::regclass AND c.relname ~ '^audit_log_[0-9]{6}$' LOOP
                IF substring(r.relname from 11) < cutoff THEN EXECUTE format('DROP TABLE app.%I', r.relname); n := n + 1; END IF;
            END LOOP;
            RETURN n;
        END $$;

        SELECT app.ensure_audit_partitions(3);

        -- Least-privilege application role: append-only on audit_log (SPEC §10.3, §12)
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bluemeal_app') THEN
                GRANT USAGE ON SCHEMA app TO bluemeal_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA app TO bluemeal_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA app TO bluemeal_app;
                REVOKE UPDATE, DELETE, TRUNCATE ON app.audit_log FROM bluemeal_app;
                REVOKE UPDATE, DELETE, TRUNCATE ON app.audit_log_default FROM bluemeal_app;
                GRANT SELECT, INSERT ON app.audit_log TO bluemeal_app;
                GRANT EXECUTE ON FUNCTION app.ensure_audit_partitions(int) TO bluemeal_app;
                REVOKE EXECUTE ON FUNCTION app.drop_old_audit_partitions(int) FROM PUBLIC;
            END IF;
        END $$;
        """;
}
