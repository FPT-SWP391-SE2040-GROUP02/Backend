# Thiết kế: Deadman switch (điểm danh định kỳ) — đợt 1 "lõi điểm danh"

Ngày: 14/10/2026. Căn cứ: SRS v3.15.0 §6.1 (F2A), DMS-01 → DMS-06, TIME-01/02/05, §8.4 (OPLAN-02), §10.1 (bảng thời hạn), §11.2 (chuyển trạng thái), `BE-FEATURE-API-HANDOFF.md` mục 7 và 13.

## 1. Mục tiêu và phạm vi

**Mục tiêu.** Owner điểm danh định kỳ. Không điểm danh thì kế hoạch bàn giao tự chuyển `ACTIVE → CHECKIN_OVERDUE → AWAITING_CHECK` và Owner được nhắc đúng mốc. Trễ điểm danh **không** mở kho, không mời người nhận (DMS-03).

**Trong đợt này**
- 3 API: xem trạng thái/lịch sử, điểm danh, đổi cấu hình chu kỳ.
- Job nền chuyển trạng thái và gửi nhắc 4 mốc (DMS-06).
- Hai hàm cho module khác gọi: mở kỳ đầu, dừng lịch.

**Ngoài đợt này** (ghi lại để không bị hiểu nhầm là thiếu)

| Việc | Lý do / ai làm |
|---|---|
| Gọi "mở kỳ đầu" khi Owner bấm Kích hoạt kế hoạch | Chưa có API kích hoạt; người làm kho gọi `StartFirstPeriodAsync` |
| Gọi "dừng lịch" trong `PlanRules.Deactivate` (đang có `TODO`) | Hàm đó là `static`, không có repository; người làm kho nối khi làm kích hoạt |
| Việc kiểm tra giao cho Executor | Chưa có module Executor. Đợt này chỉ ghi dấu `AwaitingCheckAt` trên kỳ để module đó đọc |
| Hồ sơ chứng tử, Owner báo còn sống (đặt lại kỳ), ADMIN-11 (đổi mặc định toàn hệ thống) | Đợt sau |
| Chế độ demo tăng tốc đồng hồ (TIME-08) | Đợt sau; thiết kế dùng `TimeProvider` nên thêm được |

## 2. Quyết định đã chốt

- **Job nền = `BackgroundService` của .NET** quét DB mỗi 60 giây, mọi trạng thái lưu trong DB (TIME-05). Không dùng Hangfire/Quartz.
- Hai giả định bạn chưa xác nhận, tôi chọn theo khuyến nghị (đổi được):
  1. Gửi `POST /check-ins` lặp trong **60 giây** thì trả lại kết quả cũ, không tạo kỳ mới.
  2. Kế hoạch chưa kích hoạt: `GET /check-ins/me` trả **200** với `status = NOT_STARTED` (không phải 404).

## 3. Dữ liệu (3 bảng mới)

### `CheckInSchedules` — cấu hình + mốc gần nhất, 1–1 với `HandoverPlan`
| Cột | Ý nghĩa |
|---|---|
| `PlanId` (unique) | Kế hoạch sở hữu |
| `CycleDays` | Chu kỳ đang dùng: 30/60/90, mặc định 30 |
| `GraceDays` | Thời gian chờ đang dùng: 7/14/30, mặc định 7 |
| `PendingCycleDays`, `PendingGraceDays` (null được) | Lựa chọn mới, chỉ áp dụng từ kỳ sau (DMS-01) |
| `LastCheckInAt` (null được) | Lần điểm danh gần nhất; dùng để chống gửi lặp |
| `CreatedAt`, `UpdatedAt` | `UpdatedAt` là concurrency token, cũng là ETag của cấu hình |

Dòng này tạo lười (lazy) khi cần: lần đầu `PATCH` cấu hình hoặc lần đầu mở kỳ. Chưa có dòng thì `GET` trả mặc định 30/7. Nhờ vậy **không phải sửa code tạo kho**.

### `CheckInPeriods` — mỗi kỳ một dòng (lịch sử điểm danh)
| Cột | Ý nghĩa |
|---|---|
| `PlanId`, `Sequence` | Kế hoạch và số thứ tự kỳ (1, 2, 3...) |
| `StartedAt`, `DueAt`, `GraceEndsAt` | **Chốt cứng lúc mở kỳ**; đổi cấu hình sau đó không làm đổi kỳ đang chạy |
| `CheckedInAt` | Có giá trị = Owner đã điểm danh, kỳ này đóng |
| `OverdueAt`, `AwaitingCheckAt` | Dấu thời điểm job chuyển trạng thái; chỉ ghi một lần cho mỗi kỳ (DMS-02) |
| `ClosedAt` | Kỳ đóng vì điểm danh hoặc vì dừng lịch |
| `RowVersion` | **Concurrency token** (giống `@Version` của Hibernate). Mọi thay đổi trạng thái kế hoạch do tính năng này đều cùng lúc sửa dòng kỳ, nên điểm danh và job không thể ghi đè nhau |

Ràng buộc: unique có lọc `PlanId WHERE ClosedAt IS NULL` — mỗi kế hoạch tối đa **một** kỳ đang mở; unique `(PlanId, Sequence)`.

### `CheckInReminders` — mỗi lời nhắc một dòng
`PeriodId`, `Kind` (`BEFORE_7_DAYS`, `BEFORE_1_DAY`, `GRACE_STARTED`, `GRACE_2_DAYS_LEFT`), `Status` (`PENDING`, `SENT`, `SKIPPED`), `SentAt`. Unique `(PeriodId, Kind)` → khóa duy nhất "kho + loại + mốc + kỳ" của SRS (TIME-02), nên không thể gửi trùng dù job chạy lặp hay chạy hai instance.

## 4. Luật thời gian — class thuần `CheckInTimeline` (Application, không đụng DB)

- `DueAt = StartedAt + CycleDays`, `GraceEndsAt = DueAt + GraceDays`.
- `EffectiveStatus(period, now)`: `now < DueAt` → `ACTIVE`; `DueAt ≤ now < GraceEndsAt` → `CHECKIN_OVERDUE`; `now ≥ GraceEndsAt` → `AWAITING_CHECK`. API dùng hàm này nên **trả đúng theo giờ server kể cả khi job chạy chậm** (handoff mục 13).
- Mốc nhắc: `DueAt−7d`, `DueAt−1d`, `DueAt`, `GraceEndsAt−2d`.
- Mọi giờ là UTC, lấy từ `TimeProvider` (TIME-01), không bao giờ từ trình duyệt.

## 5. API (route theo handoff mục 7; JSON camelCase, lỗi ProblemDetails như `docs/api-contract.md`)

Cả ba cần đăng nhập; kho lấy theo Owner của token, **không** nhận `userId`/`vaultId` từ client (SEC-04).

### `GET /api/v1/check-ins/me?pageIndex=&pageSize=`
```json
{
  "status": "ACTIVE",
  "planStatus": "ACTIVE",
  "serverTime": "2026-10-14T08:00:00+00:00",
  "lastCheckInAt": "2026-10-01T08:00:00+00:00",
  "nextCheckInDueAt": "2026-10-31T08:00:00+00:00",
  "graceEndsAt": "2026-11-07T08:00:00+00:00",
  "settings": { "cycleDays": 30, "graceDays": 7, "pendingCycleDays": null, "pendingGraceDays": null, "etag": "\"0\"" },
  "history": { "items": [ { "sequence": 2, "startedAt": "...", "dueAt": "...", "graceEndsAt": "...", "checkedInAt": null, "overdueAt": null, "awaitingCheckAt": null } ], "totalCount": 2, "pageIndex": 1, "pageSize": 10, "totalPages": 1, "hasPreviousPage": false, "hasNextPage": false }
}
```
- `status`: `NOT_STARTED | ACTIVE | CHECKIN_OVERDUE | AWAITING_CHECK | PAUSED`. `PAUSED` dành cho kế hoạch ở `DEATH_CASE_OPEN`, `HANDOVER_IN_PROGRESS`, `CLOSED`, `BLOCKED` (lịch không còn chạy).
- `NOT_STARTED` (kế hoạch `DRAFT`): `lastCheckInAt`, các mốc hạn là `null`, `history` rỗng, `settings` là mặc định hoặc đã chọn.
- Chưa có kho → `404 VAULT_NOT_FOUND`.

### `POST /api/v1/check-ins` (không body)
- Cho phép khi kế hoạch `ACTIVE`, `CHECKIN_OVERDUE`, `AWAITING_CHECK` (SRS §11.2), **kể cả khi dịch vụ ở `GRACE_READ_ONLY`** (OPLAN-02) → vì vậy **không** gọi `VaultRules.EnsureEditable`.
- Một transaction: đóng kỳ cũ (`CheckedInAt = ClosedAt = now`), mở kỳ mới (`StartedAt = now`, áp dụng `Pending*` rồi xóa chúng), `LastCheckInAt = now`, kế hoạch → `ACTIVE`. Trả `200` cùng body như `GET`.
- Chống gửi lặp: `now − LastCheckInAt < 60s` → trả lại trạng thái hiện tại, không tạo kỳ. Hai request **song song**: bên thua gặp `RowVersion` lệch → `409 EDIT_CONFLICT` (an toàn, không tạo kỳ trùng; FE gọi lại `GET`). Kế hoạch đang chạy mà chưa có kỳ mở (khi chức năng kích hoạt chưa nối `StartFirstPeriodAsync`) thì lần điểm danh này mở kỳ đầu.
- Lỗi: `409 CHECKIN_NOT_ALLOWED` (kèm `planStatus`) khi kế hoạch `DRAFT` hoặc đã khóa; `404 VAULT_NOT_FOUND`.

### `PATCH /api/v1/vaults/me/check-in-settings`
Body: `{ "etag": "\"0\"", "cycleDays": 60, "graceDays": 14 }` (trường nào bỏ trống thì giữ nguyên).
- `cycleDays` ∈ {30,60,90}, `graceDays` ∈ {7,14,30}, sai → `400 VALIDATION_ERROR`.
- Chưa có kỳ đang mở: áp dụng ngay. Có kỳ đang mở: ghi vào `Pending*`, **áp dụng từ kỳ sau** (DMS-01).
- Kế hoạch ở `DEATH_CASE_OPEN`, `HANDOVER_IN_PROGRESS`, `CLOSED` → `409 VAULT_NOT_EDITABLE` (reason `PLAN_LOCKED`, dùng lại luật hiện có). Etag cũ → `409 EDIT_CONFLICT`.

### Mã lỗi mới
`CHECKIN_NOT_ALLOWED` (409). Cập nhật bảng mã lỗi trong `docs/api-contract.md`.

## 6. Job nền

**`CheckInMonitor`** (Infrastructure, kế thừa `BackgroundService`): vòng lặp `delay → tạo scope → gọi ICheckInJob.RunOnceAsync(now)`; **chờ trước khi chạy lần đầu** và nuốt/ghi log mọi exception (không ghi dữ liệu nhạy cảm). Cấu hình: `CheckIn:Enabled` (mặc định `true`; test đặt `false`), `CheckIn:PollSeconds` (mặc định 60).

**`ICheckInJob`** (Application, logic thuần qua repository nên test được không cần DB) gồm `FindOpenPeriodIdsAsync` (kỳ đang mở của kế hoạch `ACTIVE`/`CHECKIN_OVERDUE` có `DueAt ≤ now + 7 ngày`) và `ProcessPeriodAsync(periodId)`. `CheckInMonitor` mở **một DI scope (một DbContext) riêng cho mỗi kỳ** và bọc `try/catch`, nên một kỳ lỗi không ảnh hưởng kỳ khác. `ProcessPeriodAsync`:
1. `ACTIVE` và `now ≥ DueAt` → ghi `OverdueAt`, kế hoạch → `CHECKIN_OVERDUE`.
2. `CHECKIN_OVERDUE` và `now ≥ GraceEndsAt` → ghi `AwaitingCheckAt`, kế hoạch → `AWAITING_CHECK`. Nếu job tắt lâu, bước 1 và 2 chạy nối tiếp trong cùng một lượt.
3. Nhắc: tính các mốc đã tới; chỉ gửi mốc **mới nhất** đã tới, các mốc cũ chưa ghi đánh dấu `SKIPPED` (tránh dội email sau khi job tắt). Ghi dòng `PENDING` rồi gọi `ICheckInReminderSender`, thành công → `SENT`; gửi lỗi → vẫn `PENDING` và exception được ném lại cho `CheckInMonitor` ghi log; vòng sau thử lại, không bao giờ có hai dòng cùng `(PeriodId, Kind)`.
4. `RowVersion` lệch (điểm danh/instance khác vừa ghi) → bỏ qua kỳ này, vòng sau đánh giá lại.
5. Kế hoạch không ở `ACTIVE`/`CHECKIN_OVERDUE` thì job **không đụng** (DMS-03, DEATH-12).

## 7. Ranh giới với module khác

| Thành phần | Hợp đồng |
|---|---|
| `ICheckInScheduler.StartFirstPeriodAsync(plan)` | Mở kỳ đầu cho kế hoạch vừa kích hoạt. Đã có kỳ mở thì **không làm gì** (SETUP-14: bấm nhiều lần chỉ một kỳ). Đặt `plan.Status = ACTIVE` do **người gọi** quyết định, hàm này chỉ mở kỳ |
| `ICheckInScheduler.StopAsync(plan)` | Đóng kỳ đang mở (`ClosedAt`), dùng khi kế hoạch về `DRAFT` (SETUP-16, chuyển Free) |
| `ICheckInReminderSender` (**interface mới**, `Application/Notifications`) | `SendReminderAsync(toEmail, fullName, kind, dueAt, graceEndsAt, ct)`. Bản cài đặt `LogCheckInReminderSender` ở Infrastructure chỉ ghi log, giống `DevEmailSender`. Dùng interface riêng thay vì thêm hàm vào `IEmailSender` để **không sửa file của người khác** và tránh xung đột merge |
| Executor | Đọc cột `CheckInPeriods.AwaitingCheckAt` khi làm module Executor |

Email nhắc chỉ chứa tên, mốc thời gian; không chứa nội dung tài sản (OPS-04).

## 8. Cấu trúc file

```
Domain/Entities/        CheckInSchedule.cs, CheckInPeriod.cs, CheckInReminder.cs
Domain/Enums/           CheckInReminderKind.cs, CheckInReminderStatus.cs
Application/Services/   CheckInTimeline.cs, ICheckInService.cs, CheckInService.cs,
                        ICheckInScheduler.cs, CheckInScheduler.cs, ICheckInJob.cs, CheckInJob.cs
Application/Repositories/ ICheckInRepository.cs
Application/Notifications/ ICheckInReminderSender.cs
Application/Dtos/       Requests/UpdateCheckInSettingsRequest.cs, Responses/CheckInStatusResponse.cs
Infrastructure/Data/Configurations/ 3 file cấu hình EF
Infrastructure/Repositories/CheckInRepository.cs
Infrastructure/Jobs/    CheckInMonitor.cs
Infrastructure/Notifications/ LogCheckInReminderSender.cs
Api/Controllers/        CheckInsController.cs  (+ PATCH settings đặt trong VaultsController hoặc controller riêng cùng route)
Migrations/             AddCheckIn (sinh từ snapshot hiện tại)
http/checkins.http; docs/api-contract.md (mục 6.8 + mã lỗi mới); README (1 dòng)
```
Chỉ **thêm dòng mới** vào `AppDbContext`, `ApplicationConfig`, `InfrastructureConfig`, không sửa dòng cũ, để giảm xung đột khi gộp nhánh. Migration sẽ phải sinh lại nếu nhánh khác thêm migration trước.

## 9. Kiểm thử

Chạy được **không cần Docker** (repository giả trong bộ nhớ + `FakeTimeProvider`, theo mẫu `GoogleAuthFakes`):
- `CheckInTimeline`: ranh giới đúng từng mốc, tính trạng thái hiệu lực, 4 mốc nhắc.
- `CheckInService`: điểm danh từ cả 3 trạng thái về `ACTIVE`; gửi lặp 60 giây; `DRAFT`/bị khóa → `CHECKIN_NOT_ALLOWED`; điểm danh khi dịch vụ chỉ đọc vẫn được; đổi cấu hình chỉ áp dụng kỳ sau; giá trị ngoài {30,60,90}/{7,14,30} bị từ chối.
- `CheckInJob`: chuyển trạng thái đúng thứ tự; chạy 2 lần không ghi trùng; job tắt lâu thì nhảy thẳng `AWAITING_CHECK` mà chỉ gửi 1 nhắc; gửi lỗi rồi thử lại không trùng; không đụng kế hoạch bị khóa.

Cần SQL thật (viết theo `[SqlFact]`, **sẽ không chạy được khi Docker còn hỏng**): unique index kỳ đang mở, `RowVersion` khi điểm danh song song, migration áp dụng được. Tôi sẽ nói rõ test nào đã chạy, test nào bị bỏ qua.

## 10. Quy ước code

Không dùng toán tử ba ngôi; dùng constructor và field tường minh, `if/else`, kiểu biến rõ ràng; comment tiếng Việt như phần còn lại của dự án.

## 11. Sản phẩm bàn giao cuối đợt

Code + test + tài liệu API, kèm **sơ đồ trạng thái và sơ đồ luồng job** cùng hướng dẫn cách đọc.
