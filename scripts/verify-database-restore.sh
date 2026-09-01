#!/usr/bin/env sh
# กู้ไฟล์ .bak ลงฐานทดสอบชั่วคราว ตรวจตารางหลัก แล้วลบทิ้ง โดยไม่แตะ RentalManager ตัวจริง
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PROJECT_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
: "${RENTAL_DATABASE_NAME:=RentalManager}"

case "$RENTAL_DATABASE_NAME" in
  ''|*[!A-Za-z0-9_]*)
    echo "RENTAL_DATABASE_NAME ใช้ได้เฉพาะ A-Z, a-z, 0-9 และ _" >&2
    exit 1
    ;;
esac

backup_file=${1:-}
if [ -z "$backup_file" ]; then
  backup_file=$(find "$PROJECT_DIR/backups/database" -maxdepth 1 -type f -name "$RENTAL_DATABASE_NAME-*.bak" -print \
    | sort | tail -1)
fi
if [ -z "$backup_file" ] || [ ! -f "$backup_file" ]; then
  echo "ไม่พบไฟล์ backup ระบุ path เช่น: bash scripts/verify-database-restore.sh backups/database/RentalManager-....bak" >&2
  exit 1
fi

timestamp=$(date -u +%Y%m%d%H%M%S)
test_database="${RENTAL_DATABASE_NAME}RestoreCheck_$timestamp"
container_backup="/var/opt/mssql/backup/restore-check-$timestamp.bak"
data_file="/var/opt/mssql/data/$test_database.mdf"
log_file="/var/opt/mssql/data/${test_database}_log.ldf"

compose() {
  docker compose --project-directory "$PROJECT_DIR" -f "$PROJECT_DIR/compose.yaml" "$@"
}

run_query() {
  compose exec -T -e RENTAL_RESTORE_QUERY="$1" sqlserver sh -c '
    set -eu
    if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
      SQLCMD=/opt/mssql-tools18/bin/sqlcmd
      TRUST=-C
    else
      SQLCMD=/opt/mssql-tools/bin/sqlcmd
      TRUST=
    fi
    "$SQLCMD" -S localhost -U sa -P "$MSSQL_SA_PASSWORD" $TRUST -b -Q "$RENTAL_RESTORE_QUERY"
  '
}

cleanup() {
  run_query "IF DB_ID(N'$test_database') IS NOT NULL BEGIN ALTER DATABASE [$test_database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$test_database]; END" >/dev/null 2>&1 || true
  compose exec -T sqlserver rm -f "$container_backup" >/dev/null 2>&1 || true
}
trap cleanup EXIT HUP INT TERM

compose exec -T sqlserver mkdir -p /var/opt/mssql/backup
compose cp "$backup_file" "sqlserver:$container_backup"
compose exec -T -u root sqlserver chown mssql:mssql "$container_backup"
compose exec -T -u root sqlserver chmod 640 "$container_backup"

restore_query="
SET NOCOUNT ON;
RESTORE DATABASE [$test_database]
FROM DISK = N'$container_backup'
WITH MOVE N'$RENTAL_DATABASE_NAME' TO N'$data_file',
     MOVE N'${RENTAL_DATABASE_NAME}_log' TO N'$log_file',
     CHECKSUM, RECOVERY;
IF OBJECT_ID(N'[$test_database].dbo.Room', N'U') IS NULL
   OR OBJECT_ID(N'[$test_database].dbo.MeterReading', N'U') IS NULL
   OR OBJECT_ID(N'[$test_database].dbo.Invoice', N'U') IS NULL
   OR OBJECT_ID(N'[$test_database].dbo.Payment', N'U') IS NULL
    THROW 51010, 'Restore completed but required tables are missing.', 1;
SELECT
  DB_NAME() AS ServerContext,
  N'$test_database' AS RestoredDatabase,
  (SELECT COUNT(*) FROM [$test_database].dbo.Room) AS Rooms,
  (SELECT COUNT(*) FROM [$test_database].dbo.Tenant) AS Tenants,
  (SELECT COUNT(*) FROM [$test_database].dbo.MeterReading) AS MeterReadings,
  (SELECT COUNT(*) FROM [$test_database].dbo.Invoice) AS Invoices,
  (SELECT COUNT(*) FROM [$test_database].dbo.Payment) AS Payments;"

run_query "$restore_query"
cleanup
trap - EXIT HUP INT TERM
echo "ทดสอบกู้คืนสำเร็จและลบฐานทดสอบแล้ว: $backup_file"
