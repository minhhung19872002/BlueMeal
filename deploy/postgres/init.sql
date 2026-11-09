-- BlueMeal PostgreSQL bootstrap (runs once, on an empty data directory, as the postgres superuser).
-- Creates: bluemeal_owner (owns schema, runs migrations) and bluemeal_app (least privilege, used by api/worker).
-- Passwords come from env via 00-env.sh (see docker-compose).
\set ON_ERROR_STOP on

SELECT format('CREATE ROLE bluemeal_owner LOGIN CREATEDB PASSWORD %L', :'owner_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bluemeal_owner') \gexec
SELECT format('CREATE ROLE bluemeal_app LOGIN PASSWORD %L', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bluemeal_app') \gexec

ALTER DATABASE bluemeal OWNER TO bluemeal_owner;
\connect bluemeal

-- Extensions (SPEC §2) — created by superuser so the owner does not need SUPERUSER
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE SCHEMA IF NOT EXISTS app AUTHORIZATION bluemeal_owner;
CREATE SCHEMA IF NOT EXISTS quartz AUTHORIZATION bluemeal_owner;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA app FROM PUBLIC;

GRANT CONNECT ON DATABASE bluemeal TO bluemeal_app;
GRANT USAGE ON SCHEMA app, quartz TO bluemeal_app;

-- Tables created later by the owner (migrations) are automatically usable by the app role.
ALTER DEFAULT PRIVILEGES FOR ROLE bluemeal_owner IN SCHEMA app GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO bluemeal_app;
ALTER DEFAULT PRIVILEGES FOR ROLE bluemeal_owner IN SCHEMA app GRANT USAGE, SELECT ON SEQUENCES TO bluemeal_app;
ALTER DEFAULT PRIVILEGES FOR ROLE bluemeal_owner IN SCHEMA app GRANT EXECUTE ON FUNCTIONS TO bluemeal_app;
ALTER DEFAULT PRIVILEGES FOR ROLE bluemeal_owner IN SCHEMA quartz GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO bluemeal_app;
-- audit_log: UPDATE/DELETE are revoked explicitly by the migration (append-only).
