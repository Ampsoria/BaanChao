# CLAUDE.md — BaanChao

> เอกสารนี้เป็น **ข้อกำหนดของระบบ** ใช้เป็น context ให้ Claude Code เวลากลับมาทำงานต่อ
> กฎทางธุรกิจในข้อ 4 คือแหล่งอ้างอิงหลัก ถ้าโค้ดกับเอกสารขัดกัน ให้ถือว่าเอกสารถูก แล้วแก้โค้ด
>
> **สถานะ:** implement แล้วครบ Phase 1–4 ในโซลูชัน `RentalManager.slnx`
> (โค้ดใช้ prefix `RentalManager` ไม่ใช่ `BaanChao` ตามที่ร่างไว้ในข้อ 1 — ชื่อในเอกสารข้อ 1 ยังไม่ได้ rename)

## สารบัญ

| # | หัวข้อ | อ่านเมื่อ |
|---|--------|----------|
| 1 | ภาพรวมโปรเจกต์ | เริ่มต้นทุกครั้ง |
| 2 | คำศัพท์ในโปรเจกต์ | ไม่แน่ใจว่าคำไทยตรงกับตัวแปรไหน |
| 3 | Tech Stack | เลือกไลบรารี |
| 4 | กฎทางธุรกิจ | **สำคัญที่สุด** — ก่อนแตะตรรกะการคำนวณ |
| 5 | วงจรการทำงานรายเดือน | ไม่แน่ใจว่างานไหนเกิดวันไหน |
| 6 | Database Schema | เขียน migration / query |
| 7 | โครงสร้างโปรเจกต์ | สร้างไฟล์ใหม่ |
| 8 | หน้า Admin | ทำ UI หรือ endpoint |
| 9 | Convention | ก่อน commit |
| 10 | Deployment | ตอนจะขึ้นเซิร์ฟเวอร์ |
| 11 | EF Core + Config | map entity / ตั้งค่า |
| 12 | Validation และ Error | เขียน validation |
| 13 | Testing | เขียนเทส |
| 14 | Roadmap | เลือกงานถัดไป |
| 15 | สิ่งที่ระบบนี้จะไม่ทำ | ก่อนเพิ่มฟีเจอร์ใหม่ |
| 16 | เรื่องที่ยังต้องตัดสินใจ | เจอจุดที่ไม่มีคำตอบในไฟล์นี้ |

---

## 1. ภาพรวมโปรเจกต์

**ชื่อโปรเจกต์: BaanChao** (บ้านเช่า)

| ที่ใช้ | ชื่อ |
|--------|------|
| Repo | `baan-chao` |
| Solution | `BaanChao.sln` |
| Root namespace | `BaanChao` |
| Database | `BaanChaoDb` |
| LINE OA display name | บ้านเช่า (ตั้งทีหลัง) |

**GitHub description**

```
Rent, utility, and deposit management for a 6-unit rental house.
ASP.NET Core + SQL Server, with prorated billing and LINE bill delivery.
```

**Topics:** `aspnet-core` `dotnet` `sql-server` `ef-core` `line-messaging-api` `promptpay` `rental-management` `billing-system` `thailand`

ระบบจัดการค่าเช่าบ้านเช่า 6 ห้อง ครอบคลุม:

- บันทึกเลขมิเตอร์น้ำ/ไฟรายเดือน
- คำนวณบิลอัตโนมัติ (ค่าเช่า + ค่าน้ำ + ค่าไฟ + ค่าขยะ)
- แจ้งบิลผ่าน LINE OA พร้อม QR พร้อมเพย์
- รับแจ้งโอน + เก็บสลิป
- ติดตามห้องที่ยังค้างชำระ

**เจ้าของ:** Amp — โปรเจกต์ใช้จริง + ใส่พอร์ตด้วย

**ลักษณะห้อง:** ห้องไม่มีแอร์ ค่าไฟต่อห้องต่อเดือนจึงไม่สูง — มีผลกับการประเมินว่าค่าน้ำ-ค่าไฟงวดสุดท้ายจะเกินมัดจำหรือไม่

---

## 2. คำศัพท์ในโปรเจกต์

เอกสารนี้เขียนภาษาไทย แต่โค้ดเป็นอังกฤษ ตารางนี้คือตัวเชื่อม — ใช้ชื่อตามนี้เสมอ อย่าคิดคำใหม่

| ไทย | โค้ด | หมายเหตุ |
|-----|------|----------|
| ห้องพัก | `Room` | มี 6 ห้อง คงที่ |
| ผู้เช่า / ลูกบ้าน | `Tenant` | หนึ่งห้องมีผู้เช่า active ได้คนเดียว |
| ค่าเช่า | `MonthlyRent` / `RentAmount` | `MonthlyRent` = ราคาตั้ง, `RentAmount` = ที่คิดจริงในบิล |
| เงินมัดจำ | `DepositAmount` | ไม่ใช้คำว่า `SecurityDeposit` |
| ใบแจ้งหนี้ / บิล | `Invoice` | |
| งวดค่าเช่า | `BillingPeriod` | `'YYYY-MM'` เดือนของค่าเช่า |
| งวดค่าน้ำ-ค่าไฟ | `UtilityPeriod` | `'YYYY-MM'` เดือนก่อนหน้า |
| เลขมิเตอร์ | `MeterReading` | `WaterCurrent`, `ElectricCurrent` |
| หน่วยที่ใช้ | `WaterUnits` / `ElectricUnits` | computed column |
| ค่าขยะ | `TrashPerMonth` / `TrashAmount` | |
| อัตราค่าบริการ | `UtilityRate` | มีประวัติ ผูก `EffectiveFrom` |
| นโยบายค่าปรับ | `BillingPolicy` | มีประวัติเช่นกัน |
| การชำระเงิน | `Payment` | |
| สลิปโอนเงิน | `Slip` | `SlipImageUrl`, `SlipHash`, `SlipRef` |
| ย้ายเข้า | move-in | `Tenant.MovedInAt` |
| ย้ายออก | move-out | `Tenant.MovedOutAt` |
| ใบสรุปตอนย้ายออก | `MoveOutSettlement` | |
| ริบมัดจำ | `IsForfeited` / `ForfeitedAmount` | |
| คืนมัดจำ | `RefundAmount` | ≥ 0 เสมอ |
| เก็บเพิ่ม | `AmountDueFromTenant` | ≥ 0 เสมอ |
| ค่าเสียหาย | `SettlementDeduction` | แจกแจงรายการ |
| เฉลี่ยตามวัน | prorate | `DaysCharged` / `DaysInPeriod` |
| เศษสตางค์ระบุห้อง | `PayeeCents` | 0.01–0.06 |
| ยอดค้างชำระ | `Outstanding` | |

---

## 3. Tech Stack

| ส่วน | เทคโนโลยี |
|------|-----------|
| Backend | ASP.NET Core (Minimal API) |
| Database | SQL Server |
| ORM | EF Core (Code First) หรือ Dapper สำหรับ query ที่ซับซ้อน |
| Scheduler | `BackgroundService` + `PeriodicTimer` |
| Chat | LINE Messaging API (webhook) |
| ตรวจสลิป | เริ่มจาก local (ZXing.Net + กันสลิปซ้ำ) แล้วค่อยเสียบ API ทีหลัง |

---

## 4. กฎทางธุรกิจ (Business Rules)

### อัตราค่าบริการปัจจุบัน

| รายการ | อัตรา | หน่วย |
|--------|-------|-------|
| ค่าน้ำ | 20.00 | บาท/หน่วย |
| ค่าไฟ | 12.00 | บาท/หน่วย |
| ค่าขยะ | 40.00 | บาท/เดือน (คงที่) |
| ค่าเช่า | แยกรายห้อง | ดูตารางด้านล่าง |

### ค่าเช่ารายห้อง

| ห้อง | ค่าเช่า/เดือน | เศษสตางค์ระบุห้อง | ยอดพื้นฐาน (เช่า + ขยะ) |
|------|--------------|-----------------|----------------------|
| 1 | 1,800 | .01 | 1,840.01 |
| 2 | 2,000 | .02 | 2,040.02 |
| 3 | 2,200 | .03 | 2,240.03 |
| 4 | 2,000 | .04 | 2,040.04 |
| 5 | 2,000 | .05 | 2,040.05 |
| 6 | 1,800 | .06 | 1,840.06 |

รวมค่าเช่า 11,800 บาท/เดือน + ค่าขยะ 240 บาท = **12,040 บาท/เดือน** (ยังไม่รวมค่าน้ำ-ค่าไฟ)

### หลักการสำคัญ

1. **อัตราต้องเก็บเป็นตารางที่มีวันที่มีผล (`EffectiveFrom`)** ไม่ใช่ hardcode ในโค้ด
   เพราะราคาน้ำ/ไฟ/ขยะเปลี่ยนได้ และต้องคำนวณบิลย้อนหลังได้ถูกต้อง

2. **ตอนออกบิล ต้อง snapshot อัตราลงในตาราง Invoice**
   ถ้าเดือนหน้าขึ้นราคา บิลเดือนเก่าต้องไม่เปลี่ยนตาม

3. **ค่าเช่าแยกรายห้อง** (`Room.MonthlyRent`) เผื่อห้องขนาดต่างกันคิดไม่เท่ากัน

4. **จำนวนหน่วย = เลขมิเตอร์ปัจจุบัน − เลขมิเตอร์ครั้งก่อน**
   ถ้าติดลบ = มิเตอร์วนรอบหรือเปลี่ยนมิเตอร์ใหม่ → ต้อง flag ให้คนตรวจ ห้ามคำนวณเงียบๆ

### เข้า-ออกกลางเดือน (Prorate)

ใช้ **รอบปฏิทิน** ทุกห้องออกบิลวันที่ 1 พร้อมกัน เพราะเดินจดมิเตอร์รอบเดียวจบ

| รายการ | เดือนที่เข้าอยู่ | เดือนที่ย้ายออก |
|--------|----------------|----------------|
| ค่าเช่า | เฉลี่ยตามวัน | **คิดเต็มเดือน ไม่คืนส่วนที่เหลือ** (จ่ายล่วงหน้าไปแล้ววันที่ 1) |
| ค่าน้ำ-ค่าไฟ | ไม่มี (เพิ่งจดเลขตั้งต้น) | คิดตามมิเตอร์จริงถึงวันย้ายออก |
| ค่าขยะ | ไม่คิด | คิดเต็ม (จ่ายไปแล้ววันที่ 1) |

```
ค่าเช่า = MonthlyRent × DaysCharged ÷ DaysInPeriod
```

- `DaysInPeriod` ใช้จำนวนวันจริงของเดือนนั้น (28/29/30/31) ไม่ใช่ 30 ตายตัว
- **ต้องจดมิเตอร์ในวันที่ลูกบ้านเข้าอยู่และวันที่ย้ายออกเสมอ** เลขนั้นคือ `WaterPrev`/`ElectricPrev` ของบิลแรก ถ้าใช้เลขของผู้เช่าคนเก่า คนใหม่จะโดนค่าน้ำค่าไฟของคนก่อน — ถ่ายรูปมิเตอร์วันส่งมอบเก็บไว้ด้วย
- **เศษวันน้อยแค่ไหนก็คิดตามจริง** ไม่ต้องมีเกณฑ์ขั้นต่ำ เพราะบิลใบแรกเก็บตอนส่งมอบห้องอยู่แล้ว ไม่ได้ออกเป็นบิลแยกใบเล็กๆ ให้ยุ่งยาก
- **ปัดค่าเช่าที่เฉลี่ยแล้วลงเป็นจำนวนเต็มบาท** (`FLOOR`) เศษสตางค์เข้าข้างผู้เช่า และเลขที่บอกลูกบ้านจะจำง่าย
- **ย้ายออกไม่เฉลี่ยและไม่คืนค่าเช่า** เพราะจ่ายล่วงหน้าไปแล้ววันที่ 1 ตอนออกจึงไม่ต้องจ่ายค่าห้องเพิ่ม และไม่คืนวันที่เหลือ
- ตรงนี้ไม่สมมาตร — เข้ากลางเดือนเฉลี่ยให้ ออกกลางเดือนไม่คืน **ต้องเขียนในสัญญาให้ชัดทั้งสองข้อ** ไม่งั้นตอนย้ายออกจะเถียงกัน
- ห้องเดียวกันเปลี่ยนผู้เช่ากลางเดือน = เดือนนั้นออกบิล **2 ใบ** (คนเก่า + คนใหม่)

### รอบจดมิเตอร์

**เดินจดวันสิ้นเดือน (30/31) ทุกห้องพร้อมกัน** แล้วออกบิลวันที่ 1

บิลใบหนึ่งจึงมีสองช่วงเวลาปนกัน — ต้องแยกให้ชัดในฐานข้อมูล ไม่งั้นจะงงตอนดูย้อนหลัง:

| ส่วนของบิลที่ออกวันที่ 1 ต.ค. | ครอบคลุมช่วง | คอลัมน์ |
|---------------------------|-------------|---------|
| ค่าเช่า + ค่าขยะ (ล่วงหน้า) | ต.ค. | `BillingPeriod = '2026-10'` |
| ค่าน้ำ + ค่าไฟ (ย้อนหลัง) | ก.ย. | `UtilityPeriod = '2026-09'` |

- `MeterReading.BillingPeriod` คือ **เดือนที่ใช้ไฟ** ไม่ใช่เดือนที่ออกบิล — จดวันที่ 30 ก.ย. = `'2026-09'`
- บิลใบแรกตอนย้ายเข้าไม่มีค่าน้ำ-ค่าไฟ → `UtilityPeriod` เป็น `NULL`
- ต้องเขียนช่วงเวลาทั้งสองลงบนใบเสร็จให้ลูกบ้านเห็น ไม่งั้นจะถามว่าทำไมค่าไฟไม่ตรงเดือน

### ค่าใช้จ่ายที่เก็บ

มีแค่ **ค่าเช่า + ค่าน้ำ + ค่าไฟ + ค่าขยะ** เท่านั้น ไม่มีค่าอินเทอร์เน็ต ค่าที่จอดรถ หรือค่าส่วนกลาง
→ ไม่ต้องทำตาราง fee แบบยืดหยุ่น ใช้คอลัมน์ตรงๆ ใน `Invoice` พอ ถ้าวันหน้ามีเพิ่มค่อยใช้ `AdjustmentAmount` ไปก่อน

### เงินมัดจำ (Deposit)

**มัดจำ = ค่าเช่า 1 เดือน** ตามค่าเช่าห้องนั้น ณ วันทำสัญญา
ห้อง 1 → 1,800 / ห้อง 2 → 2,000 / ห้อง 3 → 2,200 / ห้อง 4 → 2,000 / ห้อง 5 → 2,000 / ห้อง 6 → 1,800

หลักการ:

1. **มัดจำไม่ใช่รายได้ เป็นหนี้สิน** ห้ามนับรวมในยอดรายได้รายเดือน และห้ามใส่เป็นบรรทัดใน `Invoice` — เก็บแยกใน `Tenant.DepositAmount`
2. **Snapshot ตอนทำสัญญา** ถ้าปีหน้าขึ้นค่าเช่า มัดจำเดิมไม่เปลี่ยนตาม (นอกจากตกลงกันใหม่)
3. **หักตอนย้ายออกเท่านั้น** ห้ามหักระหว่างอยู่ ต่อให้ค้างค่าเช่า
4. รายการที่หักได้: ค่าน้ำ-ค่าไฟงวดสุดท้าย, ค่าเสียหาย/ค่าซ่อม, ค่าเช่าค้างชำระ — **ไม่มีค่าเช่างวดสุดท้าย** เพราะจ่ายล่วงหน้าไปแล้ว
5. **ค่าเสียหายต้องแจกแจงรายการ** พร้อมเหตุผลและรูปถ่าย ห้ามหักเป็นก้อนเดียวไม่มีที่มา
6. **หักแล้วไม่พอ ยังเรียกเก็บส่วนเกินได้** ระบบแยกเป็น `RefundAmount` (คืน) กับ `AmountDueFromTenant` (เก็บเพิ่ม)

### จังหวะการเก็บเงิน

เป้าหมาย: **เข้าอยู่วันไหนก็ได้ แต่เก็บเงินทุกวันที่ 1 เหมือนกันทุกห้อง** และแฟร์กับผู้เช่า

**วันเข้าอยู่ (เก็บตอนส่งมอบห้อง):**
```
มัดจำเต็มจำนวน  +  ค่าเช่าเฉลี่ยตามวัน (วันเข้าอยู่ → สิ้นเดือนนั้น)
```

**วันที่ 1 ของทุกเดือนถัดไป:**
```
ค่าเช่าเต็มเดือน (ล่วงหน้า)  +  ค่าน้ำ ค่าไฟ ของเดือนที่ผ่านมา  +  ค่าขยะ
```

ตัวอย่าง ห้อง 2 (ค่าเช่า 2,000) เข้าอยู่ 17 กันยายน (เดือนนี้ 30 วัน → อยู่ 14 วัน):

| วันที่เก็บ | รายการ | จำนวน |
|-----------|--------|-------|
| 17 ก.ย. | มัดจำ | 2,000.00 |
| 17 ก.ย. | ค่าเช่า 14/30 วัน | 933.00 |
| | **รวมวันเข้าอยู่** | **2,933.00** |
| 1 ต.ค. | ค่าเช่าเดือน ต.ค. | 2,000.00 |
| 1 ต.ค. | ค่าน้ำ-ค่าไฟ 17–30 ก.ย. | ตามมิเตอร์ |
| 1 ต.ค. | ค่าขยะ | 40.00 |

หลังจากนี้เข้ารอบปกติ ทุกวันที่ 1 เท่ากันหมด ไม่ต้องเฉลี่ยอีกจนกว่าจะย้ายออก

หมายเหตุ:

- **ค่าเช่าเก็บล่วงหน้า ค่าน้ำ-ค่าไฟเก็บย้อนหลัง** เป็นเรื่องปกติ แต่ต้องเขียนในสัญญาให้ชัดว่าบิลใบไหนครอบคลุมช่วงไหน
- **เดือนแรกไม่มีค่าน้ำ-ค่าไฟ** เพราะเพิ่งจดเลขมิเตอร์ตั้งต้น ยังไม่มีอะไรให้เทียบ
- **เดือนแรกไม่คิดค่าขยะ** เพราะอยู่ไม่เต็มเดือน

### ตอนย้ายออก

หักจากมัดจำแค่ **ค่าน้ำ + ค่าไฟ งวดสุดท้าย** (บวกค่าเสียหายถ้ามี) ไม่มีการเรียกเก็บค่าห้องเพิ่ม

| กรณี | ผลลัพธ์ |
|------|---------|
| อยู่ครบ 5 เดือนขึ้นไป | หักค่าน้ำ-ค่าไฟจากมัดจำ **คืนส่วนที่เหลือ** |
| อยู่ไม่ครบ 5 เดือน | หักค่าน้ำ-ค่าไฟจากมัดจำ **ไม่คืนส่วนที่เหลือ** (ริบ) |

ตัวอย่าง ห้อง 2 มัดจำ 2,000 ค่าน้ำ-ค่าไฟงวดสุดท้าย 380

- อยู่ 8 เดือน → คืน **1,620**
- อยู่ 3 เดือน → คืน **0** (ริบ 1,620)

**เคสที่ต้องระวัง**

1. **ค่าน้ำ-ค่าไฟมากกว่ามัดจำ** เกิดยากเพราะห้องไม่มีแอร์ แต่ถ้าเกิด ระบบต้องคำนวณส่วนเกินออกมาเป็นตัวเลขชัดๆ (`AmountDueFromTenant`) แล้วเรียกเก็บส่วนเกินนั้นต่างหาก — เก็บทั้งกรณีริบและไม่ริบ
2. **ยังไม่จ่ายค่าเช่างวดสุดท้าย** → ยอดนั้นเป็นหนี้ค้าง (`OutstandingAmount`) ต้องหักจากมัดจำด้วย ไม่ใช่ยกให้ ("ไม่ต้องจ่ายค่าห้องตอนออก" = ไม่คิดเพิ่ม ไม่ใช่ยกหนี้เก่า)
3. **ต้องจดมิเตอร์วันย้ายออก** ก่อนคืนกุญแจเสมอ ไม่งั้นคำนวณงวดสุดท้ายไม่ได้

**ผลลัพธ์แยกเป็น 2 ตัวเลข ไม่ใช้ค่าติดลบ** — `RefundAmount` (คืนผู้เช่า) กับ `AmountDueFromTenant` (เก็บเพิ่ม) ตัวใดตัวหนึ่งเป็น 0 เสมอ อ่านง่ายกว่าตัวเลขติดลบเวลาขึ้นหน้าจอหรือพิมพ์ใบแจกแจง

### ริบมัดจำ

**อยู่ไม่ครบ 5 เดือน → ริบมัดจำส่วนที่เหลือหลังหักค่าน้ำ-ค่าไฟ**

- เก็บเป็น `Tenant.MinimumStayMonths` (ค่าเริ่มต้น 5) ไม่ hardcode
- นับจาก `MovedInAt` ถึง `MoveOutDate` เก็บไว้ใน `MonthsStayed`
- **เกิน 4 เดือนครึ่งปัดขึ้นเป็น 5** → เงื่อนไขริบคือ `MonthsStayed < MinimumStayMonths − 0.5`

| อยู่จริง | ปัดเป็น | ผล |
|---------|--------|-----|
| 4 เดือน 10 วัน (4.33) | 4 | ริบ |
| 4 เดือน 16 วัน (4.53) | 5 | ไม่ริบ |
| 5 เดือน 2 วัน (5.07) | 5 | ไม่ริบ |
- **ต้องเขียนข้อนี้ในสัญญาให้ชัดตั้งแต่วันแรก** ผู้เช่าต้องรับทราบก่อนเซ็น

### ประวัติราคา

ยังไม่เคยขึ้นค่าเช่าเลยตั้งแต่เปิดให้เช่า — มัดจำจึงเท่ากับค่าเช่าปัจจุบันของทุกห้องพอดี ยังไม่มีเคสที่มัดจำเก่ากับค่าเช่าใหม่ไม่ตรงกัน

---

## 5. วงจรการทำงานรายเดือน

งานประจำที่ระบบต้องรองรับ เรียงตามวันในเดือน

| วัน | งาน | ใครทำ | เกี่ยวกับ |
|-----|-----|-------|----------|
| 28–31 | เดินจดมิเตอร์ทุกห้อง | Amp | `POST /api/admin/meter-readings` |
| 1 | ออกบิลอัตโนมัติ | ระบบ | `sp_GenerateMonthlyInvoices` |
| 1 | ส่งบิลให้ลูกบ้าน | ระบบ (ไลน์) / Amp (กระดาษ) | ดู `Tenant.PreferredChannel` |
| 1–5 | รับชำระเงิน | ลูกบ้าน | สลิปทางไลน์ หรือ Amp บันทึกเอง |
| 5 | ครบกำหนดชำระ | — | `BillingPolicy.GraceDays` |
| 6 | เตือนห้องที่ยังค้าง | ระบบ | ยังไม่มีค่าปรับ |

**งานที่เกิดเมื่อไหร่ก็ได้ (ad hoc)**

- ผู้เช่าย้ายเข้า → เก็บมัดจำ + ค่าเช่าเฉลี่ยตามวัน + จดเลขมิเตอร์ตั้งต้น + ออกบิลใบแรกทันที
- ผู้เช่าย้ายออก → จดเลขมิเตอร์ + สรุปยอด + คืนหรือริบมัดจำ
- ปรับราคา → สร้างอัตราชุดใหม่ มีผลเดือนถัดไป

**ตัวอย่างเส้นเวลาจริง**

```
30 ก.ย.  จดมิเตอร์ห้อง 1-6
 1 ต.ค.  ออกบิล = ค่าเช่า ต.ค. + ค่าน้ำค่าไฟ ก.ย. + ค่าขยะ
 1 ต.ค.  ส่งบิล + QR พร้อมเพย์ (ยอด + เศษสตางค์ประจำห้อง)
 3 ต.ค.  ห้อง 2 โอน 2,384.02 → ระบบจับคู่จากเศษ .02 ได้ทันที
 6 ต.ค.  ห้อง 5 ยังไม่จ่าย → เตือนทางไลน์
```

---

## 6. Database Schema

### ตาราง

```sql
-- ห้องพัก
CREATE TABLE Room (
    RoomId       INT IDENTITY PRIMARY KEY,
    RoomNumber   NVARCHAR(10)  NOT NULL UNIQUE,
    MonthlyRent  DECIMAL(10,2) NOT NULL,
    -- เศษสตางค์ประจำห้อง ใช้ระบุผู้โอนจากยอดเงินในสเตทเมนต์
    -- ห้อง 1 = 0.01, ห้อง 2 = 0.02, ...
    PayeeCents   DECIMAL(4,2)  NOT NULL,
    IsActive     BIT           NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Room_PayeeCents UNIQUE (PayeeCents)
);

INSERT INTO Room (RoomNumber, MonthlyRent, PayeeCents) VALUES
    (N'1', 1800.00, 0.01),
    (N'2', 2000.00, 0.02),
    (N'3', 2200.00, 0.03),
    (N'4', 2000.00, 0.04),
    (N'5', 2000.00, 0.05),
    (N'6', 1800.00, 0.06);

-- ผู้เช่า
CREATE TABLE Tenant (
    TenantId    INT IDENTITY PRIMARY KEY,
    RoomId      INT           NOT NULL REFERENCES Room(RoomId),
    FullName    NVARCHAR(200) NOT NULL,
    Phone       NVARCHAR(20)  NULL,
    LineUserId  NVARCHAR(64)  NULL,   -- ได้จาก webhook ตอนลูกบ้านทักมาครั้งแรก
    MovedInAt   DATE          NOT NULL,
    MovedOutAt  DATE          NULL,

    -- เงินมัดจำ: snapshot ค่าเช่า ณ วันทำสัญญา (ปกติ = Room.MonthlyRent × 1)
    DepositAmount     DECIMAL(10,2) NOT NULL,
    DepositReceivedAt DATE          NULL,   -- NULL = ยังไม่ได้รับมัดจำ
    DepositRefundedAt DATE          NULL,
    MinimumStayMonths TINYINT       NOT NULL DEFAULT 5,  -- อยู่ไม่ครบ = ริบมัดจำ
    -- ยังไม่รู้ว่าลูกบ้านใช้ไลน์ครบทุกคนหรือไม่ → ระบบต้องทำงานได้แม้ไม่มีไลน์
    PreferredChannel  NVARCHAR(10)  NOT NULL DEFAULT 'Paper',  -- Line | Paper
    CONSTRAINT CK_Tenant_Deposit CHECK (DepositAmount >= 0)
);
CREATE INDEX IX_Tenant_LineUserId ON Tenant(LineUserId) WHERE LineUserId IS NOT NULL;

-- อัตราค่าบริการ (มีประวัติ)
CREATE TABLE UtilityRate (
    RateId          INT IDENTITY PRIMARY KEY,
    EffectiveFrom   DATE          NOT NULL UNIQUE,
    WaterPerUnit    DECIMAL(10,2) NOT NULL,
    ElectricPerUnit DECIMAL(10,2) NOT NULL,
    TrashPerMonth   DECIMAL(10,2) NOT NULL,
    Note            NVARCHAR(200) NULL
);

INSERT INTO UtilityRate (EffectiveFrom, WaterPerUnit, ElectricPerUnit, TrashPerMonth, Note)
VALUES ('2026-01-01', 20.00, 12.00, 40.00, N'อัตราเริ่มต้น');

-- นโยบายการเก็บเงิน (มีประวัติ เหมือน UtilityRate)
-- ตอนนี้ยังไม่เก็บค่าปรับ แต่เปิดช่องไว้ให้ตั้งจากหน้า admin ได้ทีหลัง
CREATE TABLE BillingPolicy (
    PolicyId      INT IDENTITY PRIMARY KEY,
    EffectiveFrom DATE NOT NULL UNIQUE,
    GraceDays     TINYINT NOT NULL DEFAULT 5,      -- ผ่อนผันถึงวันที่เท่าไหร่ของเดือน
    LateFeeType   NVARCHAR(10) NOT NULL DEFAULT 'None',  -- None | PerDay | Flat
    LateFeeAmount DECIMAL(10,2) NOT NULL DEFAULT 0,
    LateFeeCap    DECIMAL(10,2) NULL,              -- เพดานค่าปรับ (เฉพาะ PerDay)
    Note          NVARCHAR(200) NULL,
    CONSTRAINT CK_LateFeeType CHECK (LateFeeType IN ('None','PerDay','Flat'))
);

INSERT INTO BillingPolicy (EffectiveFrom, GraceDays, LateFeeType, LateFeeAmount, Note)
VALUES ('2026-01-01', 5, 'None', 0, N'ยังไม่เก็บค่าปรับ ใช้แค่เตือนทางไลน์');

-- เลขมิเตอร์
CREATE TABLE MeterReading (
    ReadingId      INT IDENTITY PRIMARY KEY,
    RoomId         INT  NOT NULL REFERENCES Room(RoomId),
    BillingPeriod  CHAR(7) NOT NULL,        -- 'YYYY-MM' = เดือนที่ใช้ ไม่ใช่เดือนที่ออกบิล
    ReadAt         DATE NOT NULL,
    WaterPrev      DECIMAL(12,2) NOT NULL,
    WaterCurrent   DECIMAL(12,2) NOT NULL,
    ElectricPrev   DECIMAL(12,2) NOT NULL,
    ElectricCurrent DECIMAL(12,2) NOT NULL,

    -- คำนวณใน database ตามที่ต้องการ
    WaterUnits    AS (WaterCurrent    - WaterPrev)    PERSISTED,
    ElectricUnits AS (ElectricCurrent - ElectricPrev) PERSISTED,

    CONSTRAINT UQ_Reading UNIQUE (RoomId, BillingPeriod),
    CONSTRAINT CK_Water_NotNegative    CHECK (WaterCurrent    >= WaterPrev),
    CONSTRAINT CK_Electric_NotNegative CHECK (ElectricCurrent >= ElectricPrev)
);
```

> **หมายเหตุ:** `CHECK` จะบล็อกเคสมิเตอร์วนรอบ ซึ่งเป็นสิ่งที่ต้องการ — บังคับให้คนมาตรวจก่อน
> ถ้าเจอเคสนี้จริง ให้เพิ่มคอลัมน์ `IsMeterReplaced BIT` แล้วผ่อนเงื่อนไข CHECK

```sql
-- ใบแจ้งหนี้ (snapshot อัตรา ณ วันออกบิล)
CREATE TABLE Invoice (
    InvoiceId      INT IDENTITY PRIMARY KEY,
    RoomId         INT     NOT NULL REFERENCES Room(RoomId),
    TenantId       INT     NOT NULL REFERENCES Tenant(TenantId),
    BillingPeriod  CHAR(7) NOT NULL,       -- เดือนของค่าเช่า (ล่วงหน้า)
    UtilityPeriod  CHAR(7) NULL,           -- เดือนของค่าน้ำ-ค่าไฟ (ย้อนหลัง) NULL = บิลแรกตอนย้ายเข้า
    IssuedAt       DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    DueDate        DATE    NOT NULL,

    -- ช่วงที่คิดเงินจริงของ "ค่าเช่า" (รองรับเข้ากลางเดือน)
    PeriodStart    DATE    NOT NULL,
    PeriodEnd      DATE    NOT NULL,
    DaysCharged    SMALLINT NOT NULL,
    DaysInPeriod   TINYINT  NOT NULL,
    IsProrated  AS (CASE WHEN DaysCharged <> DaysInPeriod THEN 1 ELSE 0 END) PERSISTED,

    -- snapshot: ห้ามอ้างอิง UtilityRate ตอน query
    RentAmount        DECIMAL(10,2) NOT NULL,
    WaterUnits        DECIMAL(12,2) NOT NULL,
    WaterRate         DECIMAL(10,2) NOT NULL,
    ElectricUnits     DECIMAL(12,2) NOT NULL,
    ElectricRate      DECIMAL(10,2) NOT NULL,
    TrashAmount       DECIMAL(10,2) NOT NULL,
    AdjustmentAmount  DECIMAL(10,2) NOT NULL DEFAULT 0,  -- ค่าปรับ / ส่วนลด
    AdjustmentNote    NVARCHAR(200) NULL,

    -- ==== คำนวณใน database ====
    WaterAmount    AS (WaterUnits    * WaterRate)    PERSISTED,
    ElectricAmount AS (ElectricUnits * ElectricRate) PERSISTED,
    TotalAmount    AS (
        RentAmount
        + (WaterUnits    * WaterRate)
        + (ElectricUnits * ElectricRate)
        + TrashAmount
        + AdjustmentAmount
    ) PERSISTED,

    Status NVARCHAR(20) NOT NULL DEFAULT 'Unpaid',  -- Unpaid | Paid | Partial | Void

    -- ห้องเดียวกันเปลี่ยนผู้เช่ากลางเดือน = ออกได้ 2 ใบในงวดเดียว
    CONSTRAINT UQ_Invoice UNIQUE (RoomId, BillingPeriod, TenantId)
);

-- การชำระเงิน
CREATE TABLE Payment (
    PaymentId    INT IDENTITY PRIMARY KEY,
    InvoiceId    INT NOT NULL REFERENCES Invoice(InvoiceId),
    PaidAmount   DECIMAL(10,2) NOT NULL,
    PaidAt       DATETIME2 NOT NULL,
    Method       NVARCHAR(20) NOT NULL DEFAULT 'PromptPay',
    SlipImageUrl NVARCHAR(500) NULL,
    SlipHash     CHAR(64)      NULL,   -- SHA-256 กันส่งสลิปซ้ำ
    SlipRef      NVARCHAR(64)  NULL,   -- transaction ref จาก QR บนสลิป
    VerifiedBy   NVARCHAR(20)  NULL,   -- Manual | Local | ExternalApi
    RecordedAt   DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
CREATE UNIQUE INDEX UQ_Payment_SlipRef  ON Payment(SlipRef)  WHERE SlipRef  IS NOT NULL;
CREATE UNIQUE INDEX UQ_Payment_SlipHash ON Payment(SlipHash) WHERE SlipHash IS NOT NULL;

-- บันทึกการแก้ไขจากหน้า admin
-- (อัตราน้ำ/ไฟ/ขยะ มีประวัติอยู่ใน UtilityRate แล้ว ตารางนี้เก็บของอย่างอื่น เช่น ค่าเช่า)
CREATE TABLE AuditLog (
    AuditId    INT IDENTITY PRIMARY KEY,
    EntityName NVARCHAR(50)  NOT NULL,   -- 'Room' | 'UtilityRate'
    EntityKey  NVARCHAR(50)  NOT NULL,   -- RoomNumber หรือ RateId
    FieldName  NVARCHAR(50)  NOT NULL,   -- 'MonthlyRent'
    OldValue   NVARCHAR(100) NULL,
    NewValue   NVARCHAR(100) NULL,
    ChangedBy  NVARCHAR(100) NOT NULL,
    ChangedAt  DATETIME2     NOT NULL DEFAULT SYSDATETIME()
);
CREATE INDEX IX_AuditLog_Entity ON AuditLog(EntityName, EntityKey, ChangedAt DESC);

-- ใบสรุปตอนย้ายออก (หักมัดจำ)
CREATE TABLE MoveOutSettlement (
    SettlementId  INT IDENTITY PRIMARY KEY,
    TenantId      INT  NOT NULL UNIQUE REFERENCES Tenant(TenantId),
    MoveOutDate   DATE NOT NULL,
    SettledAt     DATETIME2 NOT NULL DEFAULT SYSDATETIME(),

    -- snapshot ทั้งหมด ห้ามอ้างอิงตารางอื่นตอน query
    -- หมายเหตุ: ไม่มี FinalRentAmount เพราะย้ายออกไม่คิดค่าห้องเพิ่ม
    --           ถ้างวดสุดท้ายยังไม่จ่าย ยอดนั้นจะไปอยู่ใน OutstandingAmount
    DepositAmount     DECIMAL(10,2) NOT NULL,  -- คัดลอกจาก Tenant.DepositAmount
    FinalWaterAmount  DECIMAL(10,2) NOT NULL,
    FinalElectricAmount DECIMAL(10,2) NOT NULL,
    OutstandingAmount DECIMAL(10,2) NOT NULL DEFAULT 0,  -- บิลเก่าที่ยังจ่ายไม่ครบ
    DeductionAmount   DECIMAL(10,2) NOT NULL DEFAULT 0,  -- รวมค่าเสียหาย (= SUM ของ SettlementDeduction)

    -- อยู่ไม่ครบ MinimumStayMonths → ริบส่วนที่เหลือ
    IsForfeited    BIT NOT NULL DEFAULT 0,
    ForfeitReason  NVARCHAR(200) NULL,
    MonthsStayed   DECIMAL(5,2) NOT NULL,

    -- ==== คำนวณใน database ====
    -- ยอดที่ต้องหักจากมัดจำ
    TotalDeducted AS (
        FinalWaterAmount + FinalElectricAmount + OutstandingAmount + DeductionAmount
    ) PERSISTED,

    -- แยกเป็น 2 ตัวเลข ไม่ใช้ค่าติดลบ — ตัวใดตัวหนึ่งเป็น 0 เสมอ
    -- 1) เงินที่ต้องคืนผู้เช่า (ริบ = ไม่คืน)
    RefundAmount AS (
        CASE
            WHEN IsForfeited = 1 THEN 0
            WHEN DepositAmount > (FinalWaterAmount + FinalElectricAmount
                                  + OutstandingAmount + DeductionAmount)
            THEN DepositAmount - (FinalWaterAmount + FinalElectricAmount
                                  + OutstandingAmount + DeductionAmount)
            ELSE 0
        END
    ) PERSISTED,

    -- 2) เงินที่ต้องเก็บเพิ่มจากผู้เช่า (หักแล้วยังไม่พอ) — เก็บทั้งกรณีริบและไม่ริบ
    AmountDueFromTenant AS (
        CASE
            WHEN (FinalWaterAmount + FinalElectricAmount
                  + OutstandingAmount + DeductionAmount) > DepositAmount
            THEN (FinalWaterAmount + FinalElectricAmount
                  + OutstandingAmount + DeductionAmount) - DepositAmount
            ELSE 0
        END
    ) PERSISTED,

    -- ยอดที่ริบไปจริง (ตรงนี้คือรายได้ ต่างจากตัวมัดจำที่เป็นหนี้สิน)
    ForfeitedAmount AS (
        CASE
            WHEN IsForfeited = 1
             AND DepositAmount > (FinalWaterAmount + FinalElectricAmount
                                  + OutstandingAmount + DeductionAmount)
            THEN DepositAmount - (FinalWaterAmount + FinalElectricAmount
                                  + OutstandingAmount + DeductionAmount)
            ELSE 0
        END
    ) PERSISTED,

    RefundedAt    DATETIME2 NULL,
    RefundMethod  NVARCHAR(20) NULL,
    Note          NVARCHAR(500) NULL
);

-- ค่าเสียหายแบบแจกแจงรายการ (ต้องมีเหตุผลและหลักฐานทุกรายการ)
CREATE TABLE SettlementDeduction (
    DeductionId  INT IDENTITY PRIMARY KEY,
    SettlementId INT NOT NULL REFERENCES MoveOutSettlement(SettlementId) ON DELETE CASCADE,
    Description  NVARCHAR(200)  NOT NULL,   -- 'ผนังห้องน้ำแตก', 'กุญแจหาย'
    Amount       DECIMAL(10,2)  NOT NULL,
    PhotoUrl     NVARCHAR(500)  NULL,       -- หลักฐานประกอบ
    CONSTRAINT CK_Deduction_Positive CHECK (Amount > 0)
);
```

### View สำหรับดูสถานะ

```sql
CREATE VIEW vw_InvoiceStatus AS
SELECT
    i.InvoiceId,
    r.RoomNumber,
    i.BillingPeriod,
    i.TotalAmount,
    ISNULL(p.Paid, 0)                  AS PaidAmount,
    i.TotalAmount - ISNULL(p.Paid, 0)  AS Outstanding,
    -- ยอดที่ต้องโอนจริง (บวกเศษสตางค์ประจำห้อง)
    i.TotalAmount + r.PayeeCents       AS TransferAmount,
    i.DueDate,
    CASE
        WHEN ISNULL(p.Paid, 0) >= i.TotalAmount              THEN N'ชำระแล้ว'
        WHEN ISNULL(p.Paid, 0) > 0                           THEN N'ชำระบางส่วน'
        WHEN i.DueDate < CAST(GETDATE() AS DATE)             THEN N'เกินกำหนด'
        ELSE N'รอชำระ'
    END AS StatusText
FROM Invoice i
JOIN Room r ON r.RoomId = i.RoomId
OUTER APPLY (
    SELECT SUM(PaidAmount) AS Paid
    FROM Payment WHERE InvoiceId = i.InvoiceId
) p
WHERE i.Status <> 'Void';
```

### Stored Procedure ออกบิลประจำเดือน

```sql
CREATE PROCEDURE sp_GenerateMonthlyInvoices
    @BillingPeriod CHAR(7),
    @DueDay        TINYINT = 5
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @PeriodStart DATE = CAST(@BillingPeriod + '-01' AS DATE);
    DECLARE @PeriodEnd   DATE = EOMONTH(@PeriodStart);
    DECLARE @DaysInPeriod TINYINT = DAY(@PeriodEnd);
    DECLARE @DueDate DATE = DATEADD(DAY, @DueDay - 1, @PeriodStart);
    -- จดมิเตอร์สิ้นเดือน → ค่าน้ำ-ค่าไฟในบิลใบนี้เป็นของ "เดือนก่อน"
    DECLARE @UtilityPeriod CHAR(7) = FORMAT(DATEADD(MONTH, -1, @PeriodStart), 'yyyy-MM');

    -- ดึงอัตราที่มีผล ณ ต้นงวด
    DECLARE @Water DECIMAL(10,2), @Elec DECIMAL(10,2), @Trash DECIMAL(10,2);
    SELECT TOP 1 @Water = WaterPerUnit, @Elec = ElectricPerUnit, @Trash = TrashPerMonth
    FROM UtilityRate
    WHERE EffectiveFrom <= @PeriodStart
    ORDER BY EffectiveFrom DESC;

    IF @Water IS NULL
        THROW 50001, 'ไม่พบอัตราค่าบริการที่มีผลในงวดนี้', 1;

    INSERT INTO Invoice (
        RoomId, TenantId, BillingPeriod, UtilityPeriod, DueDate,
        PeriodStart, PeriodEnd, DaysCharged, DaysInPeriod,
        RentAmount, WaterUnits, WaterRate,
        ElectricUnits, ElectricRate, TrashAmount
    )
    SELECT
        r.RoomId, t.TenantId, @BillingPeriod, @UtilityPeriod, @DueDate,
        c.ChargeStart, c.ChargeEnd, c.DaysCharged, @DaysInPeriod,
        -- ปัดลงเป็นจำนวนเต็มบาท เศษเข้าข้างผู้เช่า
        FLOOR(r.MonthlyRent * c.DaysCharged / @DaysInPeriod),
        ISNULL(m.WaterUnits, 0), @Water,
        ISNULL(m.ElectricUnits, 0), @Elec,
        -- ค่าขยะคิดเต็มเฉพาะเดือนที่อยู่ครบ
        CASE WHEN c.DaysCharged >= @DaysInPeriod THEN @Trash ELSE 0 END
    FROM Room r
    JOIN Tenant t       ON t.RoomId = r.RoomId
    -- LEFT JOIN เพราะเดือนแรกที่เริ่มใช้ระบบยังไม่มีเลขมิเตอร์ของเดือนก่อน
    LEFT JOIN MeterReading m ON m.RoomId = r.RoomId AND m.BillingPeriod = @UtilityPeriod
    CROSS APPLY (
        SELECT
            -- เข้ากลางเดือน = เฉลี่ย / ย้ายออกกลางเดือน = คิดเต็มเดือน ไม่คืนวันที่เหลือ
            ChargeStart = CASE WHEN t.MovedInAt > @PeriodStart THEN t.MovedInAt ELSE @PeriodStart END,
            ChargeEnd   = @PeriodEnd
    ) x
    CROSS APPLY (
        SELECT x.ChargeStart, x.ChargeEnd,
               DaysCharged = DATEDIFF(DAY, x.ChargeStart, x.ChargeEnd) + 1
    ) c
    WHERE r.IsActive = 1
      AND t.MovedInAt <= @PeriodEnd
      AND (t.MovedOutAt IS NULL OR t.MovedOutAt >= @PeriodStart)
      AND c.DaysCharged > 0
      -- บิลใบแรกออกไปแล้วตอนส่งมอบห้อง (sp_CreateMoveInInvoice) จะไม่ซ้ำ
      AND NOT EXISTS (
          SELECT 1 FROM Invoice i
          WHERE i.RoomId = r.RoomId
            AND i.BillingPeriod = @BillingPeriod
            AND i.TenantId = t.TenantId
      );
END;
```

### Stored Procedure สรุปยอดตอนย้ายออก

```sql
CREATE PROCEDURE sp_CreateMoveOutSettlement
    @TenantId       INT,
    @MoveOutDate    DATE,
    @WaterFinal     DECIMAL(12,2),   -- เลขมิเตอร์วันย้ายออก
    @ElectricFinal  DECIMAL(12,2)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @MovedIn DATE, @Deposit DECIMAL(10,2), @MinStay TINYINT, @RoomId INT;
    SELECT @MovedIn = MovedInAt, @Deposit = DepositAmount,
           @MinStay = MinimumStayMonths, @RoomId = RoomId
    FROM Tenant WHERE TenantId = @TenantId;

    IF @MovedIn IS NULL THROW 50002, 'ไม่พบผู้เช่ารายนี้', 1;

    -- อยู่มากี่เดือน (ทศนิยม เพื่อให้เห็นเศษเดือน)
    DECLARE @MonthsStayed DECIMAL(5,2) =
        DATEDIFF(DAY, @MovedIn, @MoveOutDate) / 30.44;

    -- อัตราที่มีผล ณ วันย้ายออก
    DECLARE @Water DECIMAL(10,2), @Elec DECIMAL(10,2);
    SELECT TOP 1 @Water = WaterPerUnit, @Elec = ElectricPerUnit
    FROM UtilityRate WHERE EffectiveFrom <= @MoveOutDate
    ORDER BY EffectiveFrom DESC;

    -- เลขมิเตอร์ครั้งล่าสุดที่จดไว้
    DECLARE @WaterPrev DECIMAL(12,2), @ElecPrev DECIMAL(12,2);
    SELECT TOP 1 @WaterPrev = WaterCurrent, @ElecPrev = ElectricCurrent
    FROM MeterReading WHERE RoomId = @RoomId
    ORDER BY BillingPeriod DESC;

    IF @WaterFinal < @WaterPrev OR @ElectricFinal < @ElecPrev
        THROW 50003, 'เลขมิเตอร์วันย้ายออกน้อยกว่าครั้งก่อน ตรวจสอบก่อน', 1;

    INSERT INTO MoveOutSettlement (
        TenantId, MoveOutDate, DepositAmount,
        FinalWaterAmount, FinalElectricAmount,
        OutstandingAmount, MonthsStayed, IsForfeited, ForfeitReason
    )
    SELECT
        @TenantId, @MoveOutDate, @Deposit,
        (@WaterFinal    - @WaterPrev) * @Water,
        (@ElectricFinal - @ElecPrev)  * @Elec,
        ISNULL(o.Outstanding, 0),
        @MonthsStayed,
        -- เกิน 4 เดือนครึ่งปัดขึ้นเป็น 5 → ไม่ริบ
        CASE WHEN @MonthsStayed < (@MinStay - 0.5) THEN 1 ELSE 0 END,
        CASE WHEN @MonthsStayed < (@MinStay - 0.5)
             THEN N'อยู่ไม่ครบ ' + CAST(@MinStay AS NVARCHAR(3)) + N' เดือน'
             ELSE NULL END
    FROM (SELECT 1 AS x) d
    OUTER APPLY (
        SELECT Outstanding = SUM(v.Outstanding)
        FROM vw_InvoiceStatus v
        JOIN Invoice i ON i.InvoiceId = v.InvoiceId
        WHERE i.TenantId = @TenantId AND v.Outstanding > 0
    ) o;

    UPDATE Tenant SET MovedOutAt = @MoveOutDate WHERE TenantId = @TenantId;
END;
```

---

## 7. โครงสร้างโปรเจกต์ (ตั้งใจไว้)

```
BaanChao/
├── BaanChao.Api/                # Minimal API + LINE webhook
│   ├── Endpoints/
│   │   ├── AdminRateEndpoints.cs
│   │   ├── AdminTenantEndpoints.cs   # ย้ายเข้า / ย้ายออก
│   │   ├── MeterEndpoints.cs
│   │   ├── InvoiceEndpoints.cs
│   │   └── LineWebhookEndpoints.cs
│   ├── wwwroot/                 # หน้า admin (ห้ามเก็บสลิปที่นี่)
│   └── Program.cs
├── BaanChao.Core/               # Domain + interfaces
│   ├── Entities/
│   ├── Interfaces/
│   │   ├── ISlipVerifier.cs
│   │   ├── ILineMessenger.cs
│   │   └── ISlipStorage.cs
│   └── Services/
│       ├── BillingService.cs        # prorate + ออกบิล
│       ├── SettlementService.cs     # ย้ายออก + มัดจำ
│       └── PromptPayQrService.cs
├── BaanChao.Infrastructure/
│   ├── Data/  (EF Core DbContext + Migrations)
│   ├── Line/  (LINE Messaging API client)
│   ├── Slip/  (LocalSlipVerifier, EasySlipVerifier)
│   └── Storage/ (LocalSlipStorage)
└── BaanChao.Tests/              # เคสตามหัวข้อ 13
```

---

## 8. หน้า Admin

หน้าจอสำหรับเจ้าของบ้านใช้เอง ครอบคลุม 3 งาน: ตั้งราคา, ย้ายเข้า-ย้ายออก, บันทึกการชำระเงิน — ทั้งหมดทำได้โดยไม่ต้องแก้โค้ดหรือ deploy ใหม่

> มี prototype UI ทำไว้แล้วที่ `BaanChaoAdmin.jsx` (React, in-memory) 2 แท็บ: ห้องพัก (ย้ายเข้า/ย้ายออก) และ ราคา — ใช้เป็นแบบอ้างอิงตอนทำของจริง

### สิ่งที่แก้ได้

| รายการ | วิธีบันทึก | เหตุผล |
|--------|-----------|--------|
| ค่าน้ำ / ค่าไฟ / ค่าขยะ | `INSERT` แถวใหม่ใน `UtilityRate` | ต้องเก็บประวัติ บิลย้อนหลังต้องคำนวณได้เหมือนเดิม |
| ค่าเช่ารายห้อง | `UPDATE Room.MonthlyRent` + เขียน `AuditLog` | `Invoice.RentAmount` snapshot ไว้แล้ว บิลเก่าไม่กระทบ |
| ค่าปรับจ่ายช้า + วันผ่อนผัน | `INSERT` แถวใหม่ใน `BillingPolicy` | เหตุผลเดียวกับอัตราค่าบริการ |

### ผู้เช่าย้ายเข้า / ย้ายออก

หน้าเดียวกันมีรายการห้องทั้ง 6 ห้อง แต่ละห้องแสดงสถานะ แล้วมีปุ่มตามสถานะ

| สถานะห้อง | ปุ่ม | ผลลัพธ์ |
|-----------|------|---------|
| ว่าง | ย้ายเข้า | `INSERT Tenant` + `INSERT MeterReading` (เลขตั้งต้น) + `INSERT Invoice` ใบแรก |
| มีผู้เช่า | ย้ายออก | `INSERT MoveOutSettlement` + `UPDATE Tenant.MovedOutAt` |

**ฟอร์มย้ายเข้า** — ชื่อ, เบอร์โทร, วันเข้าอยู่, เลขมิเตอร์น้ำ, เลขมิเตอร์ไฟ

- คำนวณสดให้เห็นก่อนกดยืนยัน: ค่าเช่าเฉลี่ยตามวัน + มัดจำ = ยอดที่ต้องเก็บวันส่งมอบห้อง
- มัดจำ = `Room.MonthlyRent` ณ วันนั้น กรอกเองไม่ได้ ป้องกันพิมพ์ผิด
- **บังคับกรอกเลขมิเตอร์** ห้ามข้าม เพราะเป็น `WaterPrev`/`ElectricPrev` ของบิลใบถัดไป
- ห้ามย้ายเข้าห้องที่ยังมีผู้เช่าอยู่

**ฟอร์มย้ายออก** — วันย้ายออก, เลขมิเตอร์ครั้งก่อน (prefill), เลขมิเตอร์วันออก, ค่าเช่าค้าง, รายการค่าเสียหาย

- คำนวณสดให้เห็นก่อนกดยืนยัน: หักอะไรบ้าง คืนเท่าไหร่ หรือเก็บเพิ่มเท่าไหร่
- แสดงชัดว่าเข้าเกณฑ์ริบมัดจำหรือไม่ พร้อมจำนวนเดือนที่อยู่จริง
- **เลขมิเตอร์วันออกน้อยกว่าครั้งก่อน = reject** ไม่ให้บันทึก
- ค่าเสียหายเพิ่มได้ทีละรายการ พร้อมช่องแนบรูป (ของจริง — prototype ยังไม่มี)
- ไม่มีช่องค่าเช่างวดสุดท้าย เพราะย้ายออกไม่คิดค่าห้องเพิ่ม

### บันทึกการชำระเงินเอง

ยังไม่แน่ใจว่าลูกบ้านใช้ไลน์ครบทุกคนหรือไม่ **ระบบต้องทำงานได้เต็มรูปแบบโดยไม่มีไลน์เลย** ไลน์เป็นชั้นเสริม ไม่ใช่ชั้นบังคับ

- หน้า admin ต้องมีปุ่มบันทึกการชำระเงินด้วยมือ (เงินสด / โอนแล้วแจ้งปากเปล่า) ไม่ต้องรอสลิปจากไลน์
- ต้องพิมพ์บิลและใบเสร็จเป็นกระดาษ/PDF ได้ สำหรับคนที่ไม่ใช้ไลน์
- `Tenant.PreferredChannel` บอกว่าคนไหนรับบิลทางไหน (`Line` / `Paper`)
- อย่าออกแบบให้ flow ไหนต้องมี `LineUserId` ถึงจะทำงานได้

### ค่าปรับจ่ายช้า

**ตอนนี้ยังไม่เก็บ** ใช้แค่เตือนทางไลน์เมื่อเลยวันผ่อนผัน แต่ทำช่องไว้ในหน้า admin ให้เปิดใช้ทีหลังได้โดยไม่ต้องแก้โค้ด

- `LateFeeType = 'None'` คือค่าเริ่มต้น — ระบบจะไม่บวกอะไรเลย
- ถ้าเปลี่ยนเป็น `PerDay` หรือ `Flat` ค่าปรับจะไปโผล่ในช่อง `Invoice.AdjustmentAmount` พร้อมข้อความใน `AdjustmentNote`
- ค่าปรับคิดจากบิลใบที่ค้าง **ไม่ทบต้นทบดอก** และมี `LateFeeCap` กันไม่ให้บานปลาย
- เปลี่ยนนโยบายแล้วต้องไม่ย้อนไปคิดค่าปรับกับบิลเก่า — ใช้ `EffectiveFrom` เหมือนอัตราค่าบริการ

### กฎการทำงาน

1. **ห้าม UPDATE ทับแถว `UtilityRate` เดิมเด็ดขาด** การแก้ราคา = สร้างอัตราชุดใหม่พร้อม `EffectiveFrom` เสมอ
2. **`EffectiveFrom` ต้องมากกว่าอัตราปัจจุบัน** ถ้าใส่ย้อนหลัง ให้ reject พร้อมข้อความบอกว่าอัตราปัจจุบันมีผลตั้งแต่เมื่อไหร่
3. **ค่าเริ่มต้นของ `EffectiveFrom` = วันที่ 1 ของเดือนถัดไป** ไม่ใช่วันนี้ เพราะงวดปัจจุบันออกบิลไปแล้ว
4. **ต้องมีหน้า preview ก่อนบันทึก** แสดงบิลตัวอย่าง 1 ห้องด้วยราคาใหม่ ให้เห็นผลกระทบก่อนกดยืนยัน
5. **ราคาต้องไม่ติดลบ** และไม่ควรเป็น 0 โดยไม่ตั้งใจ — เตือนถ้าใส่ 0

### API Endpoints

```
GET    /api/admin/rates              -> ประวัติอัตราทั้งหมด + ตัวที่ใช้อยู่
POST   /api/admin/rates              -> สร้างอัตราชุดใหม่ (body: effectiveFrom, water, electric, trash)
GET    /api/admin/rooms              -> รายการห้อง + ค่าเช่า
PATCH  /api/admin/rooms/{roomNumber} -> แก้ค่าเช่า (body: monthlyRent)
GET    /api/admin/billing-policy     -> นโยบายค่าปรับ + วันผ่อนผัน
POST   /api/admin/billing-policy     -> ตั้งค่าปรับใหม่ (body: effectiveFrom, graceDays, lateFeeType, lateFeeAmount, lateFeeCap)
POST   /api/admin/preview-invoice    -> คำนวณบิลตัวอย่าง ไม่บันทึกลง DB
GET    /api/admin/rooms/status       -> สถานะห้องทั้งหมด + ผู้เช่าปัจจุบัน
POST   /api/admin/tenants/move-in    -> ย้ายเข้า (body: roomId, name, phone, movedInAt, waterReading, electricReading)
POST   /api/admin/tenants/{id}/move-out -> ย้ายออก (body: moveOutDate, waterFinal, electricFinal, outstanding, deductions[])
POST   /api/admin/tenants/preview-move-in  -> คำนวณยอดวันส่งมอบห้อง ไม่บันทึก
POST   /api/admin/tenants/preview-move-out -> คำนวณยอดคืน/เก็บเพิ่ม ไม่บันทึก
GET    /api/admin/meter-readings/{period}  -> เลขมิเตอร์ของงวดนั้น (ทุกห้อง)
POST   /api/admin/meter-readings     -> บันทึกเลขมิเตอร์สิ้นเดือน (body: period, readings[])
POST   /api/admin/invoices/generate  -> สั่งออกบิลประจำเดือน (เรียก sp_GenerateMonthlyInvoices)
POST   /api/admin/payments           -> บันทึกการชำระเงินเอง (เงินสด/โอน ไม่ผ่านไลน์)
GET    /api/admin/invoices/{id}/print -> บิล/ใบเสร็จ PDF สำหรับคนไม่ใช้ไลน์
GET    /api/admin/audit              -> ประวัติการแก้ไข
```

### ความปลอดภัย

- **ต้องมี auth ก่อน deploy ขึ้น production** ห้ามปล่อยให้เข้าถึงด้วย URL อย่างเดียว
- 6 ห้อง ผู้ใช้คนเดียว → cookie auth + user เดียวก็พอ ไม่ต้อง Identity เต็มรูปแบบ
- **ห้ามเก็บรหัสผ่านเป็น plaintext** เก็บเป็น PBKDF2 hash ใน `Admin:PasswordHash`
  (สร้างด้วย `dotnet run --project RentalManager.Api -- hash-password`) เพราะใครอ่าน env var ได้ก็เข้าระบบได้ทันที
- **หน้า login ต้องมี rate limit** ผู้ใช้คนเดียวรหัสเดียวและเปิดสู่อินเทอร์เน็ต ถ้าไม่จำกัดก็โดนเดารหัสได้เรื่อยๆ
- ทุก endpoint ที่เขียนข้อมูลต้องบันทึก `ChangedBy` ลง `AuditLog`

---

## 9. Convention

### เก็บรูปสลิป

เก็บใน **ดิสก์ของเซิร์ฟเวอร์เอง** ไม่ผ่าน Google Drive

เหตุผล: ปีหนึ่งประมาณ 72 รูป (~15 MB) เล็กมาก ไม่คุ้มกับความยุ่งยากของ OAuth token ที่ต้อง refresh, quota ของ API, และจุดพังเพิ่มอีกจุดเวลา Drive ล่ม

วิธีเก็บ:

```
/var/baanchao/slips/2026/09/{guid}.jpg
```

- **ห้ามเก็บใน `wwwroot`** สลิปมีชื่อ เลขบัญชี เบอร์โทร ถ้าอยู่ใน wwwroot = ใครเดา URL ถูกก็เปิดดูได้
- ตั้งชื่อไฟล์เป็น **GUID** ไม่ใช่ `room3-sep.jpg` เพื่อไม่ให้เดาชื่อไฟล์ได้
- เสิร์ฟผ่าน endpoint ที่ต้อง login เท่านั้น เช่น `GET /api/slips/{paymentId}` แล้วอ่านไฟล์ส่งกลับ
- `Payment.SlipImageUrl` เก็บ **relative path** ไม่ใช่ absolute — ย้ายเซิร์ฟเวอร์แล้วไม่พัง
- **backup อัตโนมัติทุกคืน** ไปที่อื่น (rclone ขึ้น Google Drive ก็ได้ — ใช้เป็นที่สำรอง ไม่ใช่ที่เก็บหลัก) เพราะสลิปคือหลักฐานการชำระเงิน หายแล้วหายเลย
- บีบรูปให้เหลือความกว้างไม่เกิน ~1200px ก่อนเก็บ ยังอ่านออกและประหยัดที่

### ทั่วไป

- **เงินใช้ `decimal` เสมอ** ห้ามใช้ `float`/`double` ทุกกรณี
- **งวดบิลใช้ `CHAR(7)` รูปแบบ `'YYYY-MM'`** อ่านง่าย เรียงลำดับได้ตรงตัว
- **เวลาเก็บเป็น UTC ใน DB แสดงผลเป็น `Asia/Bangkok`**
- **LINE webhook ต้อง verify `x-line-signature`** (HMAC-SHA256 ด้วย Channel Secret) ก่อนประมวลผลทุกครั้ง
- **ห้าม hardcode อัตราค่าบริการในโค้ด C#** ต้องอ่านจาก `UtilityRate` เสมอ
- Secrets เก็บใน User Secrets ตอน dev, environment variables ตอน production — ห้าม commit

---

## 10. Deployment

**เป้าหมาย: ค่าใช้จ่าย 0 บาท** ระบบนี้ทำเงินได้เดือนละ 12,040 บาท แต่ไม่มีเหตุผลต้องจ่ายค่าโครงสร้างพื้นฐานตั้งแต่ยังไม่รู้ว่าจำเป็นจริงไหม

### ที่เลือกใช้

**MonsterASP.NET free tier** — ให้ครบทุกอย่างที่สแตกนี้ต้องการ

- MSSQL รวมอยู่ในแพ็กเกจ ไม่ต้องจ่ายค่า database แยก
- HTTPS ผ่าน Let's Encrypt ฟรี
- subdomain ฟรี `.runasp.net` หรือ `.tryasp.net`
- ไม่ต้องใช้บัตรเครดิต
- deploy จาก Visual Studio หรือ GitHub Actions ได้

### ไม่ซื้อ domain

- LINE สนใจแค่ว่า webhook URL เป็น HTTPS ที่มีใบรับรองถูกต้องหรือไม่ ไม่สนชื่อ domain
- subdomain ที่โฮสต์ให้มาใช้ได้ปกติ และลูกบ้านแทบไม่เห็น URL อยู่แล้วเพราะคุยผ่านไลน์
- คนที่เข้าหน้า admin มีคนเดียว

ถ้าวันหน้าอยากได้ domain เพิ่มทีหลังได้ ชี้ DNS แล้วแก้ webhook URL ในหน้า LINE Developers ช่องเดียว

### กฎสำคัญ

**ห้าม hardcode URL ในโค้ด ต้องอ่านจาก config เสมอ** (`PublicLinks:BaseUrl` คู่กับ `PublicLinks:SigningKey`)
วันย้ายโฮสต์หรือเพิ่ม domain จะได้แก้ที่เดียว — ทั้ง webhook URL และลิงก์ QR ที่ส่งเข้าไลน์ใช้ค่านี้ตัวเดียวกัน

### ขั้นตอนจริง

ดู [DEPLOYMENT.md](DEPLOYMENT.md) — env var ที่ต้องตั้ง, ตำแหน่งเก็บสลิป, backup และวิธี deploy

**ระวัง: รีโปมีทางขึ้นสองทาง อย่าผสมกัน**

| ทาง | ไฟล์ที่เป็นของทางนั้น |
|-----|---------------------|
| A. MonsterASP.NET (IIS) | `DEPLOYMENT.md`, `.github/workflows/deploy.yml`, `keepalive.yml` |
| B. Docker / Linux | `Dockerfile`, `compose.yaml`, `scripts/backup-slips.sh`, `deploy/cron/` |

ทาง B ใช้กับ IIS shared hosting ไม่ได้ ไม่มี shell ไม่มี cron และ path เป็นแบบยูนิกซ์
เก็บไว้เผื่อวันหน้าย้ายไปโฮสต์ที่รัน container ได้

### ต้องเช็คก่อนขึ้นจริง

- **Cold start** free tier หลายเจ้าพักแอปเมื่อไม่มีคนใช้ พอลูกบ้านทักไลน์ แอปต้องตื่นก่อนถึงตอบได้ ซึ่งอาจช้าเกิน timeout ของ LINE webhook แล้วข้อความแรกหาย
  มี `keepalive.yml` ping `/health` ทุก 10 นาทีแล้ว ถ้ายังเจอปัญหาบ่อยให้ย้ายไปแพ็กเกจที่ไม่หลับ
  — ส่วน**การออกบิลไม่ได้รับผลกระทบแล้ว** เพราะงานอัตโนมัติตามเก็บงวดที่ตกหล่นทุกรอบ ไม่ผูกกับวันที่ 1
- **โควตาพื้นที่** สลิปปีละประมาณ 15 MB ไม่น่ามีปัญหา แต่ต้องเช็คว่า free tier ให้เท่าไหร่
- **ตำแหน่งเก็บสลิป** ต้องอยู่นอกโฟลเดอร์ที่ deploy ไม่งั้นโดนลบทุกครั้งที่ deploy ใหม่
  relative path จะอิงโฟลเดอร์ของแอป ไม่ใช่ current directory (บน IIS ไม่แน่นอน) และแอปจะทดสอบเขียนไฟล์ให้ตอนสตาร์ต
- **Backup** free tier ส่วนใหญ่ไม่มี backup ให้ ต้องดัมพ์ฐานข้อมูล + ดึงสลิปมาสำรองเองทุกคืน
  (`scripts/backup-slips.sh` เป็นของทาง B เท่านั้น ใช้กับ IIS ไม่ได้)

### ค่าใช้จ่ายจริง

| รายการ | ค่าใช้จ่าย |
|--------|-----------|
| Hosting + MSSQL + HTTPS + subdomain | ฟรี |
| LINE OA แพ็กเกจ Free | ฟรี |
| QR พร้อมเพย์บุคคล | ฟรี |
| Backup ขึ้น Google Drive | ฟรี (15GB) |
| Domain | ไม่ซื้อ |
| **รวม** | **0 บาท** |

หมายเหตุเรื่องโควตาไลน์: แพ็กเกจ Free ให้บรอดแคสต์ราว 200–500 ข้อความต่อเดือน

---

## 11. EF Core และ Config

### Computed columns

ตารางนี้ใช้ computed column เยอะ EF Core ต้อง map ให้ถูก ไม่งั้นจะพยายาม INSERT ค่าลงไปแล้ว error

```csharp
modelBuilder.Entity<Invoice>()
    .Property(i => i.TotalAmount)
    .HasComputedColumnSql(
        "RentAmount + (WaterUnits * WaterRate) + (ElectricUnits * ElectricRate) "
      + "+ TrashAmount + AdjustmentAmount", stored: true)
    .ValueGeneratedOnAddOrUpdate();
```

คอลัมน์ที่ต้อง map แบบนี้:

- `MeterReading.WaterUnits`, `.ElectricUnits`
- `Invoice.WaterAmount`, `.ElectricAmount`, `.TotalAmount`, `.IsProrated`
- `MoveOutSettlement.TotalDeducted`, `.RefundAmount`, `.AmountDueFromTenant`, `.ForfeitedAmount`

### Decimal precision

**ตั้ง precision ทุกคอลัมน์ที่เป็นเงิน** ไม่งั้น EF Core จะเตือนและใช้ค่า default ที่ปัดเศษเพี้ยน

```csharp
configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
```

### appsettings

```json
{
  "Admin":       { "Username": "amp", "PasswordHash": "", "LoginAttemptsPerWindow": 5, "LoginWindowMinutes": 5 },
  "Billing":     { "DueDay": 5, "MinimumStayMonths": 5 },
  "Storage":     { "SlipRoot": "slips" },
  "Line":        { "ChannelSecret": "", "ChannelAccessToken": "", "Enabled": false },
  "PromptPay":   { "Target": "" },
  "PublicLinks": { "SigningKey": "", "BaseUrl": "" },
  "SlipVerification": { "External": { "Enabled": false, "Endpoint": "", "ApiKey": "" } }
}
```

- `Line:Enabled = false` ต้องทำให้ระบบยังใช้งานได้ครบ
- `SlipVerification:External:Enabled` สลับระหว่างตรวจสลิปแบบ local (ZXing) กับผ่าน API ภายนอก โดยทั้งคู่อยู่หลัง `ISlipVerifier`
- Secrets ทั้งหมดอยู่ใน User Secrets ตอน dev, environment variables ตอน production
- `Admin:PasswordHash` มีความสำคัญเหนือ `Admin:Password` ที่เป็น plaintext ซึ่งเหลือไว้เพื่อความเข้ากันได้เท่านั้น

---

## 12. Validation และ Error

### กฎที่ต้องบังคับ (ทั้งใน DB และใน API)

| กฎ | ที่บังคับ | ผลถ้าผิด |
|----|----------|----------|
| เลขมิเตอร์ปัจจุบัน ≥ ครั้งก่อน | `CK_Water_NotNegative`, `CK_Electric_NotNegative` | reject พร้อมบอกเลขทั้งสองค่า |
| หนึ่งห้องมีผู้เช่า active คนเดียว | API | reject "ห้องนี้ยังมีผู้เช่าอยู่" |
| `EffectiveFrom` ต้องหลังอัตราปัจจุบัน | API | reject พร้อมบอกวันที่ปัจจุบัน |
| ราคาไม่ติดลบ | API | reject |
| ราคา = 0 | API | เตือน แต่ผ่านได้ |
| `MoveOutDate` ≥ `MovedInAt` | API | reject |
| ออกบิลซ้ำงวดเดิม | `UQ_Invoice` | ข้ามเงียบๆ (idempotent) |
| สลิปซ้ำ | `UQ_Payment_SlipRef`, `UQ_Payment_SlipHash` | reject "สลิปนี้เคยส่งมาแล้ว" |
| ค่าเสียหาย > 0 | `CK_Deduction_Positive` | reject |

### Error codes ใน stored procedure

| Code | ความหมาย |
|------|----------|
| 50001 | ไม่พบอัตราค่าบริการที่มีผลในงวดนี้ |
| 50002 | ไม่พบผู้เช่ารายนี้ |
| 50003 | เลขมิเตอร์วันย้ายออกน้อยกว่าครั้งก่อน |

### หลักการ

- **ข้อความ error เป็นภาษาไทย** คนอ่านคือเจ้าของบ้าน ไม่ใช่ dev
- **บอกค่าที่ผิดด้วย** ไม่ใช่แค่ "ข้อมูลไม่ถูกต้อง" — เช่น "เลขมิเตอร์ไฟ 2,180 น้อยกว่าครั้งก่อน 2,310"
- **การออกบิลต้อง idempotent** สั่งซ้ำแล้วต้องไม่ได้บิลสองใบ
- **ห้าม silent fail ในการคำนวณเงิน** ถ้าข้อมูลไม่ครบให้ throw ดีกว่าคิดผิดแล้วเงียบ

---

## 13. Testing

ตรรกะเงินต้องมีเทส ไม่ต้องครบทุกบรรทัด แต่เคสพวกนี้ต้องมี

> เทสอยู่ 3 ระดับ: `BillingCalculatorTests` (ตรรกะเงินล้วน), `InvoiceGenerationTests` (การเลือกงวด/ออกบิลซ้ำ ใช้ EF InMemory)
> และ `SqlServerIntegrationTests` (computed column, constraint, unique index, view, stored procedure)
> ชุดสุดท้ายต้องมี `RENTAL_TEST_SQLSERVER` ถึงจะรัน ไม่งั้นจะถูกข้าม — CI ตั้งให้อัตโนมัติ

### การคำนวณบิล

- [x] เดือนเต็ม 30 วัน คิดค่าเช่าเต็ม
- [x] เข้าอยู่วันที่ 17 เดือน 30 วัน → `FLOOR(2000 × 14 / 30)` = 933
- [x] เข้าอยู่วันที่ 1 → ไม่ prorate
- [x] เข้าอยู่วันสุดท้ายของเดือน → คิด 1 วัน
- [x] เดือน ก.พ. 28 วัน กับปีอธิกสุรทิน 29 วัน
- [x] เดือนที่ไม่เต็ม ไม่คิดค่าขยะ
- [x] อัตราเปลี่ยนกลางทาง → บิลเก่าไม่เปลี่ยนตาม
- [x] `UtilityPeriod` เป็นเดือนก่อน `BillingPeriod` เสมอ
- [x] สั่งออกบิลซ้ำงวดเดิม → ไม่เกิดบิลใบที่สอง
- [x] ห้องเปลี่ยนผู้เช่ากลางเดือน → ออกบิล 2 ใบในงวดเดียว

### มัดจำและการย้ายออก

- [x] อยู่ 8 เดือน หัก 380 → คืน 1,620
- [x] อยู่ 3 เดือน หัก 380 → ริบ 1,620 คืน 0
- [x] อยู่ 4.53 เดือน → **ไม่ริบ** (ปัดขึ้นเป็น 5)
- [x] อยู่ 4.33 เดือน → **ริบ**
- [x] ค่าน้ำค่าไฟ 2,300 มัดจำ 2,000 → เก็บเพิ่ม 300 คืน 0
- [x] ริบ + หักเกินมัดจำ → ยังเก็บส่วนเกินได้
- [x] `RefundAmount` กับ `AmountDueFromTenant` ไม่เป็นบวกพร้อมกัน
- [x] มีค่าเช่าค้าง → หักจากมัดจำ ไม่ยกให้
- [x] ย้ายออกไม่คิดค่าเช่างวดสุดท้ายเพิ่ม

### สลิปและการชำระเงิน

- [x] ส่งสลิปเดิมซ้ำ → reject
- [x] จ่ายบางส่วน → สถานะเป็น "ชำระบางส่วน"
- [x] บันทึกเงินสดโดยไม่มีสลิป → ผ่านได้
- [x] ยอดโอนกับเศษสตางค์ระบุห้องได้ถูกห้อง

---

## 14. Roadmap

**Phase 1 — แกนหลัก**
- [x] สร้าง schema + seed ข้อมูล 6 ห้อง
- [x] CRUD บันทึกเลขมิเตอร์
- [x] `sp_GenerateMonthlyInvoices` + unit test
- [x] เคส prorate: เข้ากลางเดือน (ออกกลางเดือนไม่ต้องเฉลี่ย)
- [x] รับมัดจำตอนทำสัญญา + `sp_CreateMoveOutSettlement`
- [x] ใบแจกแจงการหักมัดจำ (แสดงทุกรายการ + รูปหลักฐาน)
- [x] หน้า admin ตั้งค่าราคา (ตามหัวข้อ 8) + auth
- [x] หน้า admin ย้ายเข้า / ย้ายออก + `sp_CreateMoveInInvoice`
- [x] หน้าเว็บ admin แบบง่าย (กรอกมิเตอร์สิ้นเดือน + ดูสถานะ)
- [x] บันทึกการชำระเงินด้วยมือ + พิมพ์บิล/ใบเสร็จ PDF

**Phase 2 — LINE** (ชั้นเสริม ระบบต้องใช้งานได้ครบโดยไม่มี Phase นี้)
- [x] ตั้ง LINE OA + webhook endpoint
- [x] ผูก `LineUserId` กับห้อง (ส่งรหัสยืนยันครั้งแรก)
- [x] Broadcast บิลรายเดือน + สร้าง QR พร้อมเพย์ระบุยอด
- [x] รับรูปสลิป → เก็บลง storage + บันทึก Payment

**Phase 3 — อัตโนมัติ**
- [x] `BackgroundService` ออกบิลวันที่ 1 อัตโนมัติ
- [x] เตือนห้องค้างชำระวันที่ 5
- [x] `LocalSlipVerifier` (ZXing + SlipHash กันซ้ำ)
- [x] เปิดใช้ค่าปรับจ่ายช้าจากหน้า admin (`BillingPolicy`)
- [x] ออกใบเสร็จ PDF

**Phase 4 — ถ้ามีงบ**
- [x] เสียบ Slip Verification API ผ่าน `ISlipVerifier`

> โค้ดครบทุก Phase แล้ว สิ่งที่เหลือเป็นงานนอกโค้ดที่ Amp ต้องทำเอง:
> สร้าง LINE OA channel จริง, เลือก hosting + HTTPS, สมัคร Slip Verification API (ถ้าจะใช้),
> และกรอกข้อมูลผู้เช่าปัจจุบัน 6 ห้อง + เลขมิเตอร์ล่าสุด (ดูข้อ 16)

---

## 15. สิ่งที่ระบบนี้จะไม่ทำ

เขียนไว้กันหลงทาง ถ้าอยากเพิ่มอะไรที่อยู่ในลิสต์นี้ ให้ถาม Amp ก่อน

- **ไม่รองรับหลายอาคาร** ออกแบบให้บ้านหลังเดียว 6 ห้อง ถ้าจะขยายต้องรื้อ schema
- **ไม่ทำบัญชี/ภาษี** ระบบนี้บอกได้แค่ใครจ่ายแล้วใครยังไม่จ่าย ไม่ออกงบการเงิน
- **ไม่มี payment gateway** รับเงินผ่านพร้อมเพย์อย่างเดียว ไม่ตัดบัตรเครดิต
- **ไม่เชื่อม API ธนาคารโดยตรง** ต้องเป็น merchant ถึงทำได้ ไม่คุ้มกับ 6 ห้อง
- **ไม่มีพอร์ทัลให้ลูกบ้าน login** ลูกบ้านใช้ไลน์หรือกระดาษเท่านั้น
- **ไม่ทำสัญญาอิเล็กทรอนิกส์** สัญญายังเซ็นกระดาษ
- **ไม่มีระบบแจ้งซ่อมเต็มรูปแบบ** แค่ทักไลน์มาก็พอ
- **ไม่ทำ mobile app** เว็บ responsive พอ

---

## 16. เรื่องที่ยังต้องตัดสินใจ

| หัวข้อ | สถานะ |
|--------|-------|
| ~~ค่าเช่าแต่ละห้อง~~ | ✅ กำหนดแล้ว (1,800–2,200) |
| ห้อง 3 แพงกว่าเพื่อนเพราะอะไร | ยังไม่ทราบ — ถ้าห้องใหญ่กว่าอาจต้องเก็บ `RoomSize` ไว้ด้วย |
| ~~รอบจดมิเตอร์~~ | ✅ สิ้นเดือน (30/31) ออกบิลวันที่ 1 |
| ~~มีค่าใช้จ่ายอื่นไหม~~ | ✅ มีแค่ค่าเช่า น้ำ ไฟ ขยะ |
| ลูกบ้านใช้ไลน์ครบทุกคนไหม | ยังไม่แน่ใจ — ออกแบบให้ระบบทำงานได้โดยไม่มีไลน์ไว้ก่อน |
| ข้อมูลผู้เช่าปัจจุบัน 6 ห้อง (ชื่อ วันเข้าอยู่ มัดจำที่ถืออยู่) | **รอ Amp ให้ข้อมูล — บล็อก Phase 1** |
| เลขมิเตอร์น้ำ-ไฟล่าสุดทุกห้อง | **รอ Amp ให้ข้อมูล — บล็อก Phase 1** |
| พร้อมเพย์ผูกเบอร์โทรหรือเลขบัตรประชาชน | รอยืนยัน (จำเป็นตอนสร้าง QR ระบุยอด) |
| สัญญาเช่าเป็นลายลักษณ์อักษรหรือปากเปล่า | ยังไม่ทราบ |
| ใครใช้ระบบบ้าง (คนเดียว / มีคนช่วย) | ยังไม่ทราบ — กระทบการออกแบบ auth |
| ~~ค่าปรับจ่ายช้า~~ | ✅ ยังไม่เก็บ ใช้เตือนทางไลน์ — ตั้งเปิดใช้ทีหลังได้จากหน้า admin (`BillingPolicy`) |
| Hosting | ✅ MonsterASP.NET free tier ไม่ซื้อ domain (ดูหัวข้อ 10) |
| ~~เก็บรูปสลิปที่ไหน~~ | ✅ ดิสก์เซิร์ฟเวอร์เอง + backup รายคืน (ดูหัวข้อ 9) |
| ~~ขึ้นค่าเช่าแล้วมัดจำเดิมต้องเพิ่มตามไหม~~ | ✅ ยังไม่เคยขึ้นค่าเช่า — เก็บ snapshot ไว้เหมือนเดิม |
| ~~มีค่าเช่าล่วงหน้าด้วยหรือเก็บแค่มัดจำ~~ | ✅ เก็บค่าเช่าเดือนแรก + มัดจำ = 2 เดือน |
| ~~ผู้เช่าออกก่อนครบสัญญา มัดจำริบไหม~~ | ✅ ริบ ถ้าอยู่ไม่ครบ 5 เดือน |
| ~~ค่าน้ำ-ค่าไฟงวดสุดท้ายเกินมัดจำ เก็บส่วนเกินไหม~~ | ✅ เก็บ — แสดงเป็น `AmountDueFromTenant` |
| ~~นับ 5 เดือนแบบไหน~~ | ✅ เกิน 4 เดือนครึ่งปัดขึ้นเป็น 5 = ไม่ริบ |
| ~~เดือนแรกคิดค่าเช่าเต็มหรือเฉลี่ยตามวัน~~ | ✅ เฉลี่ยตามวัน ปัดลงเป็นบาท เก็บตอนส่งมอบห้อง |
| ~~วันครบกำหนดชำระ~~ | ✅ ทุกวันที่ 1 ตามปฏิทิน เหมือนกันทุกห้อง (ผ่อนผันถึงวันที่ 5) |
