#!/usr/bin/env sh
set -eu

: "${RENTAL_SLIP_ROOT:=/var/rental/slips}"
: "${RENTAL_BACKUP_DESTINATION:?Set RENTAL_BACKUP_DESTINATION, for example gdrive:rental-backup/slips}"

if ! command -v rclone >/dev/null 2>&1; then
  echo "rclone is required" >&2
  exit 1
fi

rclone sync "$RENTAL_SLIP_ROOT" "$RENTAL_BACKUP_DESTINATION" \
  --checksum \
  --create-empty-src-dirs \
  --log-level INFO
