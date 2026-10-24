#!/bin/sh
# Proves a backup can be restored: loads a dump into a scratch database and checks that the data is there. Nothing existing is touched.
#   scripts/restore-drill.sh /backups/mindlift-20261024-020000.dump
# Run inside the backup container (docker compose -f docker-compose.prod.yml exec backup sh /scripts/restore-drill.sh <dump>),
# with PGHOST, PGUSER, PGPASSWORD set. Exit code 0 means the restore worked and the key tables have rows.
set -eu
dump="${1:?Give the dump file to test}"
scratch="restore_drill_$(date -u +%Y%m%d%H%M%S)"
started=$(date +%s)
trap 'psql -d postgres -qc "drop database if exists \"$scratch\"" >/dev/null 2>&1 || true' EXIT

psql -d postgres -qc "create database \"$scratch\""
pg_restore --no-owner --dbname="$scratch" "$dump"

fail=0
for table in tenants users tenant_settings; do
  n=$(psql -d "$scratch" -Atc "select count(*) from $table")
  echo "$table: $n rows"
  [ "$n" -gt 0 ] || { echo "FAILED: $table is empty"; fail=1; }
done
migrations=$(psql -d "$scratch" -Atc 'select count(*) from "__EFMigrationsHistory"')
echo "migrations applied: $migrations"
echo "restore took $(( $(date +%s) - started )) seconds"
[ "$fail" -eq 0 ] && echo "RESTORE DRILL PASSED" || { echo "RESTORE DRILL FAILED"; exit 1; }
