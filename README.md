# Rental Manager

ระบบจัดการค่าเช่าบ้านเช่า 6 ห้องตาม [CLAUDE.md](CLAUDE.md) พัฒนาด้วย ASP.NET Core MVC/Web API, EF Core และ SQL Server โดยฟังก์ชันใน Phase 1–4 มี implementation ครบแล้ว

โครงสร้างเป็น MVC และแยกความรับผิดชอบดังนี้:

- `RentalManager.Api/Controllers`, `Models`, `Views` — HTTP API และหน้า Admin MVC
- `RentalManager.Core` — entity, interface และกฎคำนวณที่ไม่ผูกกับฐานข้อมูล
- `RentalManager.Infrastructure` — EF Core, SQL Server, LINE, PromptPay, storage, slip verifier และ PDF
- `RentalManager.Tests` — unit, service, MVC smoke และ SQL Server integration tests

## ความสามารถหลัก

- ย้ายเข้าแบบ prorate, snapshot มัดจำ และจดมิเตอร์ตั้งต้น
- CRUD มิเตอร์, ออกบิล, เก็บประวัติราคา/นโยบาย และ audit log
- บิลงวด M คิดค่าเช่าล่วงหน้าของเดือน M แต่คิดค่าน้ำ-ค่าไฟของเดือน M−1 (`Invoice.UtilityPeriod`)
  ผู้เช่าที่เพิ่งย้ายเข้าเดือนนี้จะไม่ถูกคิดหน่วยของผู้เช่าคนก่อน
- `Tenant.PreferredChannel` (`Line`/`Paper`) กำหนดว่าใครรับบิลทางไหน ระบบทำงานได้ครบแม้ไม่มี LINE เลย
- พิมพ์บิลเป็น PDF ได้ที่ `GET /api/admin/invoices/{id}/print` สำหรับผู้เช่าที่รับบิลเป็นกระดาษ
- ย้ายออก หักค่าน้ำ/ไฟ/หนี้/ค่าเสียหาย พร้อมรูปหลักฐานและ PDF แจกแจง
- PromptPay QR ที่คิดจากยอดคงเหลือและเศษสตางค์ประจำห้อง
- LINE webhook แบบตรวจ HMAC, รหัสผูกห้อง, ส่งบิล/เตือน และรับรูปสลิป
- เก็บสลิปนอก `wwwroot`, GUID filename, resize, SHA-256 ป้องกันสลิปซ้ำ
- ตรวจสลิป local ด้วย ZXing; รายการที่ยืนยันไม่ได้จะเป็น `Pending` ให้ Admin ตรวจ
- adapter สำหรับ External Slip Verification API ผ่าน `ISlipVerifier`
- `BackgroundService` ออกบิลวันที่ 1, ค่าปรับตาม policy และเตือนเมื่อเกินกำหนด
- ใบเสร็จและใบสรุปย้ายออก PDF

## เริ่มใช้งานสำหรับพัฒนา

ต้องมี .NET 10 SDK และ SQL Server 2022 ขึ้นไป จากนั้นตั้งค่า secrets:

```bash
dotnet tool restore
dotnet user-secrets set --project RentalManager.Api "ConnectionStrings:RentalDb" "Server=localhost;Database=RentalManager;User Id=sa;Password=...;TrustServerCertificate=True"
dotnet user-secrets set --project RentalManager.Api "Admin:Username" "amp"
# สร้างค่า hash แล้วนำไปใส่ Admin:PasswordHash (พิมพ์รหัสผ่านทาง stdin จะได้ไม่ตกค้างใน shell history)
dotnet run --project RentalManager.Api -- hash-password
dotnet user-secrets set --project RentalManager.Api "Admin:PasswordHash" "pbkdf2$210000$...$..."
dotnet user-secrets set --project RentalManager.Api "PromptPay:Target" "0812345678"
dotnet user-secrets set --project RentalManager.Api "PublicLinks:SigningKey" "at-least-32-random-characters"
dotnet user-secrets set --project RentalManager.Api "PublicLinks:BaseUrl" "https://your-public-host.example"
dotnet run --project RentalManager.Api
```

ค่าที่เป็นกฎทางธุรกิจอยู่ใน `appsettings.json` ส่วน `Billing` ไม่ได้ hardcode ในโค้ด:

| คีย์ | ค่าเริ่มต้น | ความหมาย |
|------|-----------|----------|
| `Billing:DueDay` | 5 | วันครบกำหนดชำระ ใช้เมื่อ `BillingPolicy.GraceDays` ยังไม่ได้ตั้ง |
| `Billing:MinimumStayMonths` | 5 | ระยะพักขั้นต่ำ อยู่ไม่ครบ = ริบมัดจำส่วนที่เหลือ (เก็บ snapshot ลง `Tenant` ตอนย้ายเข้า) |
| `Admin:LoginAttemptsPerWindow` | 5 | จำนวนครั้งที่ลองล็อกอินได้ต่อ IP ต่อหนึ่งหน้าต่างเวลา เกินแล้วตอบ 429 |
| `Admin:LoginWindowMinutes` | 5 | ความยาวหน้าต่างเวลาของการนับข้างบน |

`Admin:PasswordHash` มีความสำคัญกว่า `Admin:Password` เสมอ ถ้ายังไม่ได้ตั้ง hash ระบบจะยังยอมใช้ plaintext
เพื่อความเข้ากันได้กับ config เดิม แต่จะเขียน warning ตอนสตาร์ตเมื่อไม่ได้อยู่ใน Development

การออกบิลอัตโนมัติจะตามเก็บงวดปัจจุบันและงวดก่อนหน้าทุกรอบ ไม่ได้ผูกกับวันที่ 1
แอปที่ถูกพักตอนไม่มีคนใช้แล้วตื่นมาทีหลังจึงยังออกบิลของเดือนนั้นให้ครบ

เมื่อ `Database:InitializeOnStartup=true` แอปจะใช้ EF migrations สร้าง/อัปเกรด schema, seed ห้อง 1–6, view และ stored procedures ให้อัตโนมัติ หน้า Admin อยู่ที่ URL ราก และ health check อยู่ที่ `/health`

ถ้าต้องการจัดการ migration เอง:

```bash
dotnet ef database update --project RentalManager.Infrastructure --startup-project RentalManager.Api
```

## รันด้วย Docker Compose

```bash
cp .env.example .env
# แก้ค่าทุกตัวใน .env โดยเฉพาะรหัสผ่านและ signing key
docker compose up --build -d
curl http://localhost:8080/health
```

Production ต้องวางหลัง HTTPS reverse proxy ค่า cookie จะเป็น Secure เสมอเมื่อไม่ใช่ Development และไม่ควรเปิดพอร์ต SQL Server ออกสู่สาธารณะ Compose เปิด forwarded headers ไว้สำหรับ proxy แล้ว จึงต้องใช้ firewall ให้ request ภายนอกผ่าน proxy เท่านั้น

## ตั้งค่า LINE OA

ส่วนเชื่อมต่อพร้อมใช้งาน แต่ต้องสร้าง Messaging API channel ใน LINE Developers ด้วยบัญชีของผู้ดูแลเอง:

1. ตั้ง webhook เป็น `https://<public-host>/api/line/webhook`
2. เปิด Use webhook และปิด greeting/auto-reply ที่ซ้ำกับ bot ตามต้องการ
3. ตั้ง `Line:Enabled=true`, `Line:ChannelSecret` และ `Line:ChannelAccessToken`
4. ตั้ง `PublicLinks:BaseUrl` เป็น HTTPS URL ที่ LINE เข้าถึงได้ และใช้ signing key ที่สุ่มอย่างน้อย 32 ตัวอักษร
5. ในหน้า Admin กด “รหัสผูก LINE” แล้วให้ผู้เช่าส่ง `ผูกห้อง 123456` ภายใน 15 นาที

ระบบตรวจ `x-line-signature` ก่อนอ่าน event ทุกครั้ง และลิงก์ QR สาธารณะมี HMAC token พร้อมวันหมดอายุ

## External Slip Verification API

เปิดผ่านค่าต่อไปนี้:

```text
SlipVerification:External:Enabled=true
SlipVerification:External:Endpoint=https://provider.example/verify
SlipVerification:External:ApiKey=...
```

adapter ส่ง multipart field ชื่อ `file` พร้อม Bearer token และรองรับ response ที่มี `amount`/`paidAmount`, `transRef`/`reference`/`transactionId`, และ `transferredAt`/`transDate` ที่ root หรือใต้ `data` หาก provider ใช้ contract อื่นให้แก้เฉพาะ `ExternalSlipVerifier` โดยไม่กระทบ flow หลัก

## Backup สลิป

สลิปและรูปหลักฐานอยู่ใน `/var/rental/slips/YYYY/MM/{guid}.jpg` สำหรับ container และไม่ถูกเสิร์ฟเป็น static file ใช้ [scripts/backup-slips.sh](scripts/backup-slips.sh) ร่วมกับ rclone; ตัวอย่าง cron อยู่ที่ [deploy/cron/backup-slips.cron](deploy/cron/backup-slips.cron)

ควรใช้ปลายทาง backup คนละเครื่อง/ผู้ให้บริการ และทดสอบ restore เป็นระยะ

## ตรวจสอบระบบ

```bash
dotnet build RentalManager.slnx --no-restore
dotnet test RentalManager.slnx --no-build
node --check RentalManager.Api/wwwroot/app.js
docker compose config
```

GitHub Actions ที่ [.github/workflows/ci.yml](.github/workflows/ci.yml) รันชุดเดียวกันนี้ทุก push/PR
โดยยก SQL Server ขึ้นเป็น service container และตั้ง `RENTAL_TEST_SQLSERVER` ให้ จึงรัน integration test จริงไม่ข้าม

SQL integration test จะถูกข้ามหากไม่กำหนดฐานข้อมูลทดสอบ:

```bash
RENTAL_TEST_SQLSERVER='Server=localhost,1433;Database=RentalManagerIntegration;User Id=sa;Password=...;TrustServerCertificate=True' \
  dotnet test RentalManager.Tests/RentalManager.Tests.csproj --filter FullyQualifiedName~SqlServerIntegrationTests
```

อย่าชี้ integration test ไปฐานข้อมูล production เวลาในฐานข้อมูลเก็บเป็น UTC, UI แสดง Asia/Bangkok และจำนวนเงินใช้ `decimal` ทุกจุด

## สิ่งที่ต้องมีตอนนำขึ้นใช้จริง

- hostname/HTTPS reverse proxy และ firewall
- ค่า Admin, SQL Server, PromptPay และ signing key ที่ไม่ใช่ค่าตัวอย่าง
- LINE OA credentials หากต้องการเปิด Phase 2
- API key/contract ของผู้ให้บริการ หากต้องการเปิด external slip verification ใน Phase 4
- cron/rclone backup และการเฝ้าดู `/health`/application logs
