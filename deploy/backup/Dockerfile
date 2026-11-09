# Backup sidecar: same major as the database so pg_dump/pg_restore match (SPEC §10.7).
FROM postgres:16-alpine
RUN apk add --no-cache openssl tzdata coreutils
COPY backup.sh restore.sh entrypoint.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/*.sh && mkdir -p /backups && chown postgres:postgres /backups
ENV TZ=Asia/Ho_Chi_Minh BACKUP_CRON="0 1 * * *" KEEP_DAILY=7 KEEP_WEEKLY=4 KEEP_MONTHLY=6
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
