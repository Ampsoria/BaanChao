#!/usr/bin/env bash
# สร้างไฟล์ .env สำหรับยกระบบขึ้นชั่วคราวให้คนอื่นลองใช้ (เช่นใน GitHub Codespaces)
#
#   bash scripts/make-demo-env.sh https://xxxx-8080.app.github.dev
#
# ค่าลับทุกตัวสุ่มใหม่ทุกครั้ง และ .env ถูก gitignore ไว้แล้ว จึงไม่หลุดขึ้นรีโป
# ใช้สำหรับ demo ที่รื้อทิ้งทีหลังเท่านั้น ไม่ใช่ของ production
set -euo pipefail

BASE_URL="${1:-}"
if [ -z "$BASE_URL" ]; then
  echo "ใช้: bash scripts/make-demo-env.sh <PUBLIC_BASE_URL>" >&2
  echo "ตัวอย่าง: bash scripts/make-demo-env.sh https://xxxx-8080.app.github.dev" >&2
  exit 1
fi
BASE_URL="${BASE_URL%/}"

ENV_FILE="$(cd "$(dirname "$0")/.." && pwd)/.env"
if [ -e "$ENV_FILE" ]; then
  echo "มี .env อยู่แล้วที่ $ENV_FILE — ลบหรือย้ายออกก่อนถ้าต้องการสร้างใหม่" >&2
  exit 1
fi

# สุ่มอักขระที่ปลอดภัยกับ shell และ connection string (ไม่มี ; ' " $ \)
# ใช้ subshell เพื่อปิด pipefail เฉพาะตรงนี้ เพราะ head ปิด pipe ใส่ tr แล้วได้ SIGPIPE
random_token() (
  set +o pipefail
  LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c "${1:?}"
)

# SQL Server บังคับความซับซ้อน: ต้องมีพิมพ์ใหญ่ พิมพ์เล็ก ตัวเลข และอักขระพิเศษ
SA_PASSWORD="Db$(random_token 20)9x!"
ADMIN_PASSWORD="Demo$(random_token 14)7k"
SIGNING_KEY="$(random_token 48)"

umask 077
cat > "$ENV_FILE" <<EOF
# สร้างโดย scripts/make-demo-env.sh — สำหรับ demo ชั่วคราวเท่านั้น
MSSQL_SA_PASSWORD=$SA_PASSWORD
ADMIN_USERNAME=amp
ADMIN_PASSWORD=$ADMIN_PASSWORD
PROMPTPAY_TARGET=0812345678
PUBLIC_LINK_SIGNING_KEY=$SIGNING_KEY
PUBLIC_BASE_URL=$BASE_URL
LINE_ENABLED=false
LINE_CHANNEL_SECRET=
LINE_CHANNEL_ACCESS_TOKEN=
SLIP_API_ENABLED=false
SLIP_API_ENDPOINT=
SLIP_API_KEY=
EOF

cat <<EOF

สร้าง .env แล้ว

  ที่อยู่ระบบ   $BASE_URL
  ผู้ใช้        amp
  รหัสผ่าน      $ADMIN_PASSWORD

ขั้นต่อไป:
  docker compose up --build -d
  curl -s -o /dev/null -w '%{http_code}\\n' http://localhost:8080/health

แล้วตั้ง visibility ของพอร์ต 8080 เป็น Public ในแท็บ PORTS ก่อนส่งลิงก์ให้คนอื่น

เลิกใช้แล้วรื้อทิ้งด้วย:
  docker compose down -v && rm .env
EOF
