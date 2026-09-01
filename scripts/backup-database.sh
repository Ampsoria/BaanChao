#!/usr/bin/env sh
# สำรอง SQL Server ของ Docker Compose เป็นไฟล์ .bak โดยใช้ CHECKSUM และตรวจไฟล์ก่อนคัดลอกออกมา
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PROJECT_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
: "${RENTAL_DATABASE_NAME:=RentalManager}"
: "${RENTAL_DATABASE_BACKUP_DIR:=$PROJECT_DIR/backups/database}"

case "$RENTAL_DATABASE_NAME" in
  ''|*[!A-Za-z0-9_]*)
    echo "RENTAL_DATABASE_NAME ใช้ได้เฉพาะ A-Z, a-z, 0-9 และ _" >&2
    exit 1
    ;;
esac

timestamp=$(date -u +%Y%m%dT%H%M%SZ)
filename="$RENTAL_DATABASE_NAME-$timestamp.bak"
container_path="/var/opt/mssql/backup/$filename"
local_path="$RENTAL_DATABASE_BACKUP_DIR/$filename"

compose() {
  docker compose --project-directory "$PROJECT_DIR" -f "$PROJECT_DIR/compose.yaml" "$@"
}

mkdir -p "$RENTAL_DATABASE_BACKUP_DIR"

cleanup() {
  # target มาจากชื่อฐานข้อมูลที่ validate แล้วและ timestamp ที่สคริปต์สร้างเอง
  compose exec -T sqlserver rm -f "$container_path" >/dev/null 2>&1 || true
}
trap cleanup EXIT HUP INT TERM

backup_query="BACKUP DATABASE [$RENTAL_DATABASE_NAME] TO DISK = N'$container_path' WITH COPY_ONLY, INIT, CHECKSUM, COMPRESSION"
verify_query="RESTORE VERIFYONLY FROM DISK = N'$container_path' WITH CHECKSUM"

compose exec -T -e RENTAL_BACKUP_QUERY="$backup_query" -e RENTAL_VERIFY_QUERY="$verify_query" sqlserver sh -c '
  set -eu
  mkdir -p /var/opt/mssql/backup
  if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
    SQLCMD=/opt/mssql-tools18/bin/sqlcmd
    TRUST=-C
  elif [ -x /opt/mssql-tools/bin/sqlcmd ]; then
    SQLCMD=/opt/mssql-tools/bin/sqlcmd
    TRUST=
  else
    echo "ไม่พบ sqlcmd ใน SQL Server container" >&2
    exit 1
  fi
  "$SQLCMD" -S localhost -U sa -P "$MSSQL_SA_PASSWORD" $TRUST -b -Q "$RENTAL_BACKUP_QUERY"
  "$SQLCMD" -S localhost -U sa -P "$MSSQL_SA_PASSWORD" $TRUST -b -Q "$RENTAL_VERIFY_QUERY"
'

compose cp "sqlserver:$container_path" "$local_path"

if [ -n "${RENTAL_DATABASE_BACKUP_DESTINATION:-}" ]; then
  if ! command -v rclone >/dev/null 2>&1; then
    echo "ตั้ง RENTAL_DATABASE_BACKUP_DESTINATION แล้ว แต่ไม่พบ rclone" >&2
    exit 1
  fi
  rclone copyto "$local_path" "${RENTAL_DATABASE_BACKUP_DESTINATION%/}/$filename" \
    --checksum --log-level INFO
fi

echo "สำรองและตรวจสอบฐานข้อมูลแล้ว: $local_path"
