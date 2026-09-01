# Deployment

เอกสารนี้ขยายจาก [CLAUDE.md หัวข้อ 10](CLAUDE.md) ให้เป็นขั้นตอนที่ทำตามได้จริง

รีโปนี้รองรับการ deploy **สองทาง** เลือกอย่างใดอย่างหนึ่ง อย่าผสมกัน

| ทาง | ใช้เมื่อ | ไฟล์ที่เกี่ยวข้อง |
|-----|---------|-----------------|
| **A. MonsterASP.NET (IIS shared hosting)** | ตามที่เลือกไว้ใน CLAUDE.md ข้อ 10 — ค่าใช้จ่าย 0 บาท | เอกสารนี้ + `.github/workflows/deploy.yml` |
| **B. Docker / Linux** | ถ้าย้ายไปโฮสต์ที่รัน container ได้ | `Dockerfile`, `compose.yaml`, `scripts/backup-database.sh`, `scripts/backup-slips.sh`, `deploy/cron/` |

> ไฟล์ Docker, สคริปต์ backup และ crontab ใน `deploy/cron/` เป็นของทาง B ทั้งหมด
> ใช้กับ IIS shared hosting ไม่ได้ (ไม่มี shell, ไม่มี cron, path เป็นแบบยูนิกซ์)

---

## ทาง A — MonsterASP.NET

> **ตรวจแพ็กเกจก่อนใช้จริง:** หน้า pricing ของผู้ให้บริการอาจเปลี่ยนได้ และปัจจุบันแยก HTTPS
> ออกจาก Free tier ในตารางเปรียบเทียบ ห้ามนำระบบ Admin หรือ LINE webhook ขึ้น HTTP ล้วน
> ถ้า site ฟรีที่ได้รับไม่มี HTTPS ให้เปลี่ยนเป็นแพ็กเกจ/ผู้ให้บริการที่มี HTTPS ก่อนกรอกข้อมูลจริง

### 1. เตรียมฝั่งโฮสต์

1. สมัครและสร้าง site — จะได้ subdomain `.runasp.net` หรือ `.tryasp.net`
2. ยืนยันว่าแพ็กเกจเปิด HTTPS ได้ แล้วเปิดใบรับรองในแผงควบคุม **ต้องทำก่อนกรอกข้อมูลจริงหรือตั้ง LINE webhook**
3. สร้างฐานข้อมูล MSSQL แล้วจดค่า connection string ไว้
4. ดาวน์โหลด publish profile (`.PublishSettings`) สำหรับ Web Deploy

### 2. ตั้ง environment variables ในแผงควบคุม

ห้ามใส่ค่าพวกนี้ลงไฟล์ในรีโป ใช้ชื่อแบบขีดล่างคู่เพื่อแทนเครื่องหมาย `:`

```text
ConnectionStrings__RentalDb   = Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True
Admin__Username               = amp
Admin__PasswordHash           = pbkdf2$210000$...$...
PromptPay__Target             = 08xxxxxxxx
PublicLinks__SigningKey       = (สุ่มอย่างน้อย 32 ตัวอักษร)
PublicLinks__BaseUrl          = https://<ชื่อ-site>.runasp.net
Storage__SlipRoot             = ..\data\slips
Database__InitializeOnStartup = true
Line__Enabled                 = false
```

สร้างค่า `Admin__PasswordHash` ด้วย (พิมพ์รหัสผ่านทาง stdin ไม่ตกค้างใน shell history):

```bash
dotnet run --project RentalManager.Api -- hash-password
```

### 3. เรื่องที่ต้องระวังเป็นพิเศษ

**ตำแหน่งเก็บสลิป** — `Storage__SlipRoot` ต้องชี้ไปนอกโฟลเดอร์ที่ deploy
ไม่งั้นสลิปจะถูกลบทุกครั้งที่ deploy ใหม่ และสลิปคือหลักฐานการชำระเงิน หายแล้วหายเลย

- relative path จะอิงโฟลเดอร์ของแอป (content root) ไม่ใช่ current directory
  จึงใช้ `..\data\slips` เพื่อออกไปอยู่ระดับเดียวกับโฟลเดอร์เว็บได้
- ตอนสตาร์ตแอปจะทดสอบเขียนไฟล์แล้วเขียน log บอกผล
  ถ้าเห็น `เขียนโฟลเดอร์เก็บสลิปไม่ได้` ให้แก้สิทธิ์ก่อนรับสลิปจากลูกบ้าน
- Web Deploy ต้องไม่ลบโฟลเดอร์นี้ — workflow ที่ให้มาใส่ `-skip` ไว้แล้ว

**Migration** — ตั้ง `Database__InitializeOnStartup=true` เพราะบน shared hosting
รัน `dotnet ef database update` ไม่ได้ แอปจะอัปเกรด schema ให้เองตอนสตาร์ต

**Cold start** — free tier พักแอปเมื่อไม่มีคนใช้ มีผลสองอย่าง

1. *การออกบิล* — แก้แล้ว งานอัตโนมัติตามเก็บงวดที่ตกหล่นทุกรอบ ไม่ผูกกับวันที่ 1
   ต่อให้แอปหลับข้ามวันที่ 1 พอตื่นมาก็ยังออกบิลของเดือนนั้นให้ครบ
2. *LINE webhook* — ยังเสี่ยงอยู่ ถ้าแอปหลับ ข้อความแรกที่ลูกบ้านทักอาจช้าเกิน timeout แล้วหาย
   ใช้ `.github/workflows/keepalive.yml` ping `/health` เป็นระยะเพื่อลดโอกาส
   (GitHub cron เป็น best-effort อาจคลาดเคลื่อนได้ ถ้าเจอปัญหาบ่อยให้ย้ายไปแพ็กเกจที่ไม่หลับ)

**Backup** — free tier ส่วนใหญ่ไม่มี backup ให้ ต้องทำเอง สองส่วน

- *ฐานข้อมูล* — ใช้เครื่องมือ backup/export ในแผงควบคุมของโฮสต์ตามรอบที่ตั้งได้
- *สลิป* — ดึงลงมาผ่าน FTP แล้วสำรองต่อ หรือใช้ `scripts/backup-slips.sh` ได้เฉพาะกรณีย้ายไปทาง B

### 4. Deploy

workflow `.github/workflows/deploy.yml` เป็นแบบ **สั่งเองเท่านั้น** (`workflow_dispatch`)
ไม่ deploy อัตโนมัติตอน push เพื่อไม่ให้ขึ้น production โดยไม่ตั้งใจ

ก่อนใช้ครั้งแรกต้องใส่ secrets ในหน้า repo (Settings → Secrets and variables → Actions):

| Secret | ค่าที่ใส่ |
|--------|----------|
| `MSDEPLOY_URL` | `https://<server>:8172/msdeploy.axd?site=<sitename>` |
| `MSDEPLOY_SITE` | ชื่อ site จาก publish profile |
| `MSDEPLOY_USERNAME` | จาก publish profile |
| `MSDEPLOY_PASSWORD` | จาก publish profile |

จากนั้นไปที่แท็บ Actions → Deploy → Run workflow

> workflow นี้ยังไม่เคยรันจริง เพราะต้องมี credential ของโฮสต์ก่อน
> ครั้งแรกให้ดู log แล้วปรับ `MSDEPLOY_URL` ตามที่โฮสต์กำหนด

### 5. ตรวจหลัง deploy

```bash
curl https://<ชื่อ-site>.runasp.net/health
```

แล้วเข้าหน้า admin ที่ URL ราก ล็อกอินด้วย `Admin__Username` / รหัสผ่านที่ใช้สร้าง hash

จากนั้นถ้าจะเปิด LINE (Phase 2)

1. ตั้ง webhook เป็น `https://<ชื่อ-site>.runasp.net/api/line/webhook`
2. ตั้ง `Line__Enabled=true`, `Line__ChannelSecret`, `Line__ChannelAccessToken`
3. กด Verify ในหน้า LINE Developers

---

## ทาง B — Docker

ดู [README.md](README.md) หัวข้อ "รันด้วย Docker Compose"

---

## ถ้าวันหน้าซื้อ domain

1. ชี้ DNS มาที่โฮสต์ แล้วผูก domain ในแผงควบคุม + ออกใบรับรอง HTTPS ใหม่
2. แก้ `PublicLinks__BaseUrl` เป็น domain ใหม่
3. แก้ webhook URL ในหน้า LINE Developers

ไม่ต้องแก้โค้ดเลย เพราะทั้ง URL ของ QR และ webhook อ่านจาก `PublicLinks:BaseUrl` ค่าเดียว
