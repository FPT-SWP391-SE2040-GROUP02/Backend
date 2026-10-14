# Deadman switch (lõi điểm danh) — Kế hoạch triển khai

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Owner điểm danh định kỳ; không điểm danh thì kế hoạch tự chuyển `ACTIVE → CHECKIN_OVERDUE → AWAITING_CHECK` và Owner được nhắc đúng 4 mốc, qua 3 API và một job nền.

**Architecture:** Controller → Service → Repository → DB như phần còn lại của dự án. Luật thời gian nằm trong class thuần `CheckInTimeline`. Job nền `CheckInMonitor` (BackgroundService) quét DB mỗi 60 giây, mỗi kỳ xử lý trong DI scope riêng; mọi trạng thái nằm trong DB nên chạy lặp hoặc hai instance không tạo trùng.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10 + SQL Server, xUnit. Không thêm package mới.

**Spec:** `docs/superpowers/specs/2026-10-14-deadman-switch-design.md` (SRS v3.15.0 §6.1, DMS-01..06, TIME-01/02/05, OPLAN-02, §10.1, §11.2).

## Điều chỉnh so với spec (do cách code thật hoạt động)

1. `ICheckInJob` tách thành `FindOpenPeriodIdsAsync` + `ProcessPeriodAsync(periodId)` để `CheckInMonitor` mở **một DI scope (một DbContext) cho mỗi kỳ** — một kỳ lỗi không làm bẩn DbContext của kỳ khác. Bỏ giới hạn 100 kỳ/vòng (kỳ chưa tới hạn nhắc đã bị lọc trong truy vấn, không cần giới hạn).
2. Điểm danh **song song** hai request: bên thua nhận `409 EDIT_CONFLICT` (do `IUnitOfWork` hiện có đổi `DbUpdateConcurrencyException`), không tạo kỳ trùng. Gửi lặp **tuần tự** trong 60 giây vẫn trả trạng thái hiện tại.
3. `POST /check-ins` khi kế hoạch đang chạy (`ACTIVE`/`CHECKIN_OVERDUE`/`AWAITING_CHECK`) mà **chưa có kỳ mở** (vì chức năng kích hoạt chưa nối `StartFirstPeriodAsync`) thì mở kỳ đầu từ thời điểm điểm danh.
4. Gửi nhắc lỗi: `CheckInJob` để dòng ở `PENDING` rồi **ném lại exception**; `CheckInMonitor` ghi log. (Application không có logger.)

## Global Constraints

- **Không dùng toán tử ba ngôi `? :`**; không dùng `??`/`?.` để thay thế logic phức tạp. Code dễ đọc cho người quen Java: constructor + field tường minh (không primary constructor), `if/else`, kiểu biến ghi rõ (hạn chế `var` khi kiểu không hiển nhiên).
- Comment/thông báo lỗi tiếng Việt, theo giọng văn các file hiện có.
- Chu kỳ ∈ {30, 60, 90} ngày (mặc định 30); thời gian chờ ∈ {7, 14, 30} ngày (mặc định 7); đổi chỉ áp dụng từ kỳ sau (DMS-01).
- 4 mốc nhắc (DMS-06): `DueAt−7 ngày`, `DueAt−1 ngày`, `DueAt` (bắt đầu chờ), `GraceEndsAt−2 ngày`; mỗi (kỳ, mốc) gửi đúng một lần (TIME-02).
- Mọi giờ lưu UTC, lấy từ `TimeProvider`; không tin giờ trình duyệt (TIME-01). Đăng nhập/thanh toán không phải điểm danh (DMS-02).
- Điểm danh **không** gọi `VaultRules.EnsureEditable` (cho phép cả khi dịch vụ `GRACE_READ_ONLY`, OPLAN-02).
- Chỉ **thêm dòng mới** vào `AppDbContext`, `ApplicationConfig`, `InfrastructureConfig`, `PlanRules`: không sửa dòng cũ (giảm xung đột merge).
- API: camelCase, lỗi ProblemDetails qua `AppException` + `ErrorCode`; không bao giờ nhận `userId`/`vaultId` từ client (SEC-04).
- Lệnh test: `dotnet test tests/LegacyVault.IntegrationTests --filter "<Tên>"` (chạy cả solution mất ~1 phút vì test health chờ timeout). Máy này Docker đang hỏng → test `[SqlFact]` bị Skip; phải báo rõ khi tổng kết.
- **Không `git commit`/`push` trừ khi người dùng yêu cầu.** Bước "Checkpoint" chỉ chạy build + test.

## Review Focus

Các tình huống spec ngầm đòi hỏi nhưng dễ bị bỏ sót (mỗi dòng đã có test ở task ghi trong ngoặc):

1. Job tắt nhiều ngày rồi bật lại: chỉ gửi **một** nhắc (mốc mới nhất), mốc cũ đánh `SKIPPED`, không dội email (Task 5).
2. Gửi nhắc ném exception: dòng ở `PENDING`, vòng sau gửi lại đúng một lần, không có hai dòng cùng mốc (Task 5).
3. Điểm danh đúng giây `DueAt` / `GraceEndsAt` (ranh giới `≥`), và sát 60 giây (cửa sổ chống lặp) (Task 2, Task 3).
4. `PATCH` với giá trị 0, âm, ngoài danh sách, body không đổi gì, và etag sai định dạng; đổi cấu hình khi đang có kỳ mở **không** làm đổi `DueAt` của kỳ đó (Task 4).
5. Kế hoạch `DRAFT` / `DEATH_CASE_OPEN` / `CLOSED`: `POST` bị từ chối, `GET` trả `NOT_STARTED`/`PAUSED`, job không đụng tới (Task 3, Task 5).

---

## File Structure

```
src/LegacyVault.Domain/Entities/CheckInSchedule.cs        (mới)
src/LegacyVault.Domain/Entities/CheckInPeriod.cs          (mới)
src/LegacyVault.Domain/Entities/CheckInReminder.cs        (mới)
src/LegacyVault.Domain/Enums/CheckInReminderKind.cs       (mới)
src/LegacyVault.Domain/Enums/CheckInReminderStatus.cs     (mới)
src/LegacyVault.Application/Services/CheckInRules.cs      (mới)  hằng số + luật trạng thái
src/LegacyVault.Application/Services/CheckInTimeline.cs   (mới)  tính giờ thuần
src/LegacyVault.Application/Repositories/ICheckInRepository.cs (mới)
src/LegacyVault.Application/Notifications/ICheckInReminderSender.cs (mới)
src/LegacyVault.Application/Dtos/Requests/UpdateCheckInSettingsRequest.cs (mới)
src/LegacyVault.Application/Dtos/Responses/CheckInStatusResponse.cs (mới) + CheckInSettingsResponse, CheckInPeriodResponse
src/LegacyVault.Application/Services/ICheckInService.cs, CheckInService.cs (mới)
src/LegacyVault.Application/Services/ICheckInScheduler.cs, CheckInScheduler.cs (mới)
src/LegacyVault.Application/Services/ICheckInJob.cs, CheckInJob.cs (mới)
src/LegacyVault.Application/Exceptions/ErrorCode.cs       (thêm giá trị CheckInNotAllowed ở CUỐI enum)
src/LegacyVault.Infrastructure/Data/Configurations/CheckInScheduleConfiguration.cs, CheckInPeriodConfiguration.cs, CheckInReminderConfiguration.cs (mới)
src/LegacyVault.Infrastructure/Data/AppDbContext.cs       (thêm 3 DbSet)
src/LegacyVault.Infrastructure/Repositories/CheckInRepository.cs (mới)
src/LegacyVault.Infrastructure/Notifications/LogCheckInReminderSender.cs (mới)
src/LegacyVault.Infrastructure/Jobs/CheckInJobSettings.cs, CheckInMonitor.cs (mới)
src/LegacyVault.Infrastructure/Data/Migrations/*_AddCheckIn*.cs (sinh bằng dotnet ef)
src/LegacyVault.Api/Controllers/CheckInsController.cs     (mới)
src/LegacyVault.Api/appsettings.Development.json          (thêm mục "CheckIn")
tests/LegacyVault.IntegrationTests/CheckIn/CheckInTimelineTests.cs, CheckInFakes.cs, CheckInSchedulerTests.cs, CheckInServiceTests.cs, CheckInSettingsTests.cs, CheckInJobTests.cs, CheckInMonitorTests.cs, CheckInFlowTests.cs (mới)
http/checkins.http; docs/api-contract.md (mục 6.8 + mã lỗi); README.md (1 dòng)
```

---

### Task 1: Dữ liệu — entity, enum, cấu hình EF, repository, migration

**Files:**
- Create: 3 entity, 2 enum, 3 `*Configuration.cs`, `ICheckInRepository.cs`, `CheckInRepository.cs` (đường dẫn ở File Structure)
- Modify: `AppDbContext.cs` (thêm 3 `DbSet`), `InfrastructureConfig.cs` (thêm `services.AddScoped<ICheckInRepository, CheckInRepository>();` cạnh các repository khác)

**Interfaces — Produces:**
```csharp
// Domain/Enums
public enum CheckInReminderKind { DueInSevenDays = 0, DueInOneDay = 1, GraceStarted = 2, GraceTwoDaysLeft = 3 }  // thứ tự = thứ tự thời gian
public enum CheckInReminderStatus { Pending = 0, Sent = 1, Skipped = 2 }

// Domain/Entities (class thường, Id = Guid.CreateVersion7() như các entity khác)
CheckInSchedule { Guid Id; Guid PlanId; HandoverPlan Plan; int CycleDays = 30; int GraceDays = 7;
                  int? PendingCycleDays; int? PendingGraceDays; DateTimeOffset? LastCheckInAt;
                  DateTimeOffset CreatedAt; DateTimeOffset? UpdatedAt; }          // UpdatedAt: concurrency token + etag
CheckInPeriod   { Guid Id; Guid PlanId; HandoverPlan Plan; int Sequence; DateTimeOffset StartedAt, DueAt, GraceEndsAt;
                  DateTimeOffset? CheckedInAt, OverdueAt, AwaitingCheckAt, ClosedAt; byte[] RowVersion;
                  List<CheckInReminder> Reminders; }
CheckInReminder { Guid Id; Guid PeriodId; CheckInPeriod Period; CheckInReminderKind Kind;
                  CheckInReminderStatus Status; DateTimeOffset CreatedAt; DateTimeOffset? SentAt; }

// Application/Repositories/ICheckInRepository.cs
Task<CheckInSchedule?> FindScheduleByPlanIdAsync(Guid planId, CancellationToken ct);
Task<CheckInPeriod?> FindOpenPeriodByPlanIdAsync(Guid planId, CancellationToken ct);   // ClosedAt == null
Task<int> GetMaxSequenceAsync(Guid planId, CancellationToken ct);                      // chưa có kỳ → 0
Task<(List<CheckInPeriod> Items, int TotalCount)> FindPeriodPageByPlanIdAsync(Guid planId, int pageIndex, int pageSize, CancellationToken ct); // Sequence giảm dần, AsNoTracking
Task<IReadOnlyList<Guid>> FindOpenPeriodIdsForJobAsync(DateTimeOffset now, CancellationToken ct);
Task<CheckInPeriod?> FindPeriodForJobAsync(Guid periodId, CancellationToken ct);       // kèm Plan, Plan.Vault, Plan.Vault.Owner, Reminders (có theo dõi)
void AddSchedule(CheckInSchedule schedule);
void AddPeriod(CheckInPeriod period);
void AddReminder(CheckInReminder reminder);
```

- [ ] **Step 1: Tạo 2 enum và 3 entity** đúng chữ ký trên; comment tiếng Việt nêu rõ: `RowVersion` là concurrency token (như `@Version` của Hibernate), `DueAt`/`GraceEndsAt` chốt cứng lúc mở kỳ, `CheckInSchedule.UpdatedAt` là etag.
- [ ] **Step 2: Tạo 3 `IEntityTypeConfiguration`.** Giá trị bắt buộc: bảng `CheckInSchedules` / `CheckInPeriods` / `CheckInReminders`; enum lưu chuỗi (`HasConversion<string>().HasMaxLength(32)`); `CheckInSchedule`: unique `PlanId`, `UpdatedAt.IsConcurrencyToken()`; `CheckInPeriod`: `RowVersion.IsRowVersion()`, unique `(PlanId, Sequence)`, unique **có lọc** `PlanId` với `.HasFilter("[ClosedAt] IS NULL")`, index `(DueAt)`; `CheckInReminder`: unique `(PeriodId, Kind)`. Khóa ngoại tới `HandoverPlans` dùng `DeleteBehavior.Restrict`; `CheckInReminder → CheckInPeriod` dùng `Cascade`.
- [ ] **Step 3: Thêm 3 `DbSet` vào `AppDbContext`** (nhóm mới "Điểm danh") và cài `CheckInRepository` (primary constructor theo mẫu `VaultRepository` được; hoặc constructor thường). `FindOpenPeriodIdsForJobAsync`: kỳ có `ClosedAt == null`, `Plan.Status` là `Active` hoặc `CheckinOverdue`, và `DueAt <= now + 7 ngày`; trả danh sách Id.
- [ ] **Step 4: Build.** Run: `dotnet build` — Expected: `Build succeeded`, 0 error.
- [ ] **Step 5: Sinh migration.** Run: `dotnet ef migrations add AddCheckIn --project src/LegacyVault.Infrastructure --startup-project src/LegacyVault.Api --output-dir Data/Migrations -- --environment Development` — Expected: `Done.`; mở file `*_AddCheckIn.cs` kiểm tra có `CreateTable` cho đúng 3 bảng và `CreateIndex` có `filter: "[ClosedAt] IS NULL"`. Chưa chạy được `database update` khi Docker hỏng — ghi rõ khi tổng kết.
- [ ] **Step 6: Checkpoint:** `dotnet build` pass; không commit.

---

### Task 2: `CheckInRules` và `CheckInTimeline` (luật thời gian thuần)

**Files:**
- Create: `Application/Services/CheckInRules.cs`, `Application/Services/CheckInTimeline.cs`
- Test: `tests/LegacyVault.IntegrationTests/CheckIn/CheckInTimelineTests.cs`

**Interfaces — Produces:**
```csharp
public static class CheckInRules {
  public const int DefaultCycleDays = 30;  public const int DefaultGraceDays = 7;
  public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(60);
  public static bool IsAllowedCycleDays(int days);     // 30, 60, 90
  public static bool IsAllowedGraceDays(int days);     // 7, 14, 30
  public static bool IsRunning(PlanStatus s);          // Active, CheckinOverdue, AwaitingCheck (dùng lại PlanRules.IsRunning)
  public static bool IsPaused(PlanStatus s);           // DeathCaseOpen, HandoverInProgress, Closed, Blocked
  public static bool IsSettingsLocked(PlanStatus s);   // DeathCaseOpen, HandoverInProgress, Closed
}
public static class CheckInTimeline {
  public static DateTimeOffset ComputeDueAt(DateTimeOffset startedAt, int cycleDays);
  public static DateTimeOffset ComputeGraceEndsAt(DateTimeOffset dueAt, int graceDays);
  public static PlanStatus EffectiveStatus(DateTimeOffset dueAt, DateTimeOffset graceEndsAt, DateTimeOffset now); // Active | CheckinOverdue | AwaitingCheck
  public static DateTimeOffset MilestoneAt(CheckInReminderKind kind, DateTimeOffset dueAt, DateTimeOffset graceEndsAt);
  public static CheckInReminderKind? LatestReachedMilestone(DateTimeOffset dueAt, DateTimeOffset graceEndsAt, DateTimeOffset now);
}
```

- [ ] **Step 1: Viết test (đỏ)** trong `CheckInTimelineTests`, mốc gốc `start = 2026-10-01T08:00Z`, `cycle=30`, `grace=7` → `due = 2026-10-31T08:00Z`, `graceEnds = 2026-11-07T08:00Z`:
  - `ComputeDueAt_AddsCycleDays`: kết quả `== due`; `ComputeGraceEndsAt_AddsGraceDays`: `== graceEnds`.
  - `EffectiveStatus_BoundariesUseGreaterOrEqual`: `due−1s → Active`; **`due → CheckinOverdue`**; `graceEnds−1s → CheckinOverdue`; **`graceEnds → AwaitingCheck`**.
  - `MilestoneAt_ReturnsFourSpecMilestones`: `DueInSevenDays = due−7d`, `DueInOneDay = due−1d`, `GraceStarted = due`, `GraceTwoDaysLeft = graceEnds−2d`.
  - `LatestReachedMilestone_ReturnsNullBeforeFirst`: `due−7d−1s → null`; `LatestReachedMilestone_PicksNewestReached`: `due−7d → DueInSevenDays`, `due−1d → DueInOneDay`, `due → GraceStarted`, `graceEnds−2d → GraceTwoDaysLeft`, `graceEnds+30d → GraceTwoDaysLeft`.
  - `Rules_AcceptOnlySpecValues` (`[Theory]`): cycle 30/60/90 true; 0, −30, 31, 45, 120 false; grace 7/14/30 true; 0, −7, 8, 21, 60 false.
  - `Rules_PlanStatusGroups`: `IsRunning` đúng 3 trạng thái; `IsPaused` đúng 4; `IsSettingsLocked` đúng 3 (`Blocked` **không** khóa cấu hình, `Draft` không thuộc nhóm nào).
- [ ] **Step 2: Run:** `dotnet test tests/LegacyVault.IntegrationTests --filter "FullyQualifiedName~CheckInTimelineTests"` — Expected: lỗi biên dịch (chưa có lớp).
- [ ] **Step 3: Cài hai lớp static** theo chữ ký; `MilestoneAt` dùng `switch` câu lệnh (không biểu thức `?:`); `LatestReachedMilestone` duyệt 4 mốc từ mới đến cũ.
- [ ] **Step 4: Run lại cùng lệnh** — Expected: tất cả PASS.
- [ ] **Step 5: Checkpoint:** `dotnet build` pass.

---

### Task 3: Dữ liệu giả cho test, `CheckInScheduler` và `ICheckInReminderSender`

**Files:**
- Create: `Application/Notifications/ICheckInReminderSender.cs`, `Application/Services/ICheckInScheduler.cs`, `CheckInScheduler.cs`
- Test: `tests/.../CheckIn/CheckInFakes.cs`, `CheckInSchedulerTests.cs`

**Interfaces — Produces:**
```csharp
public interface ICheckInReminderSender {
  Task SendReminderAsync(string toEmail, string fullName, CheckInReminderKind kind,
                         DateTimeOffset dueAt, DateTimeOffset graceEndsAt, CancellationToken ct);
}
public interface ICheckInScheduler {
  /// Mở kỳ đầu cho kế hoạch (đã có kỳ mở → không làm gì, SETUP-14). KHÔNG đổi plan.Status, KHÔNG SaveChanges: người gọi lưu.
  Task StartFirstPeriodAsync(HandoverPlan plan, CancellationToken ct);
  /// Đóng kỳ đang mở (ClosedAt = now). Không SaveChanges.
  Task StopAsync(HandoverPlan plan, CancellationToken ct);
}
public sealed class CheckInScheduler : ICheckInScheduler {
  public CheckInScheduler(ICheckInRepository repository, TimeProvider timeProvider);
}
```
**Fakes** (`namespace LegacyVault.IntegrationTests.CheckIn`; dùng lại `FixedTimeProvider`, `FakeUnitOfWork` có sẵn ở `GoogleAuth/GoogleAuthFakes.cs`):
`FakeCurrentUser : ICurrentUser` (`Guid? UserId {get;set;}`, `IsAuthenticated` = `UserId != null`); `FakeVaultRepository : IVaultRepository` (`List<Vault> Vaults`; `FindByOwnerIdAsync`/`FindByIdAsync` tìm trong list; `TryAddAsync` thêm và trả true; `SumUsedBytesAsync` trả 0); `FakeCheckInRepository : ICheckInRepository` (3 `List` công khai; `FindOpenPeriodIdsForJobAsync` lọc đúng như Task 1 Step 3; `FindPeriodForJobAsync` trả kỳ đã gắn `Plan`/`Reminders`; `Add*` thêm vào list và với `AddReminder` thêm cả vào `period.Reminders`); `FakeCheckInReminderSender : ICheckInReminderSender` (`List<CheckInReminderKind> Sent`; `bool FailNext` → lần gọi kế ném `InvalidOperationException` rồi tự tắt); static `CheckInTestData.CreateVault(Guid ownerId, PlanStatus status)` trả `Vault` đã gắn `Plan`, `Plan.Vault`, `Owner` (email `owner@test.local`).

- [ ] **Step 1: Viết `CheckInFakes.cs`** theo mô tả trên (cần `ICheckInRepository` đã có từ Task 1 và `ICheckInReminderSender` — tạo interface trước).
- [ ] **Step 2: Viết test (đỏ)** `CheckInSchedulerTests`: `now = 2026-10-01T08:00Z`.
  - `StartFirstPeriod_CreatesSequenceOnePeriodWithDefaults`: plan `Active`, chưa có schedule → có 1 `CheckInPeriod` `Sequence=1`, `StartedAt=now`, `DueAt = now+30d`, `GraceEndsAt = DueAt+7d`; có 1 `CheckInSchedule` (30/7) cho plan.
  - `StartFirstPeriod_UsesScheduleSettings`: schedule sẵn `CycleDays=60, GraceDays=14` → `DueAt = now+60d`, `GraceEndsAt = DueAt+14d`.
  - `StartFirstPeriod_CalledTwice_KeepsSingleOpenPeriod`: gọi 2 lần → đúng 1 kỳ.
  - `Stop_ClosesOpenPeriod`: kỳ mở → `ClosedAt == now`, `CheckedInAt == null`; gọi `Stop` khi không có kỳ mở → không lỗi.
- [ ] **Step 3: Run** `--filter "FullyQualifiedName~CheckInSchedulerTests"` — Expected: FAIL (chưa có `CheckInScheduler`).
- [ ] **Step 4: Cài `CheckInScheduler`:** `StartFirstPeriodAsync` đọc schedule (chưa có → tạo mới với mặc định `CheckInRules.Default*`, `CreatedAt = now`, `AddSchedule`), nếu đã có kỳ mở thì return, ngược lại `AddPeriod` với `Sequence = GetMaxSequenceAsync + 1`, hạn tính bằng `CheckInTimeline`.
- [ ] **Step 5: Run lại** — Expected: PASS.
- [ ] **Step 6: Checkpoint:** `dotnet build` pass.

---

### Task 4: `CheckInService` — xem trạng thái và điểm danh

**Files:**
- Create: `Application/Dtos/Responses/CheckInStatusResponse.cs`, `Application/Services/ICheckInService.cs`, `CheckInService.cs`
- Modify: `ErrorCode.cs` (thêm `CheckInNotAllowed` ở **cuối** enum)
- Test: `tests/.../CheckIn/CheckInServiceTests.cs`

**Interfaces — Produces:**
```csharp
public class CheckInStatusResponse {
  public string Status; public string PlanStatus; public DateTimeOffset ServerTime;
  public DateTimeOffset? LastCheckInAt, NextCheckInDueAt, GraceEndsAt;
  public CheckInSettingsResponse Settings; public PaginatedList<CheckInPeriodResponse> History;
}
public class CheckInSettingsResponse { public int CycleDays, GraceDays; public int? PendingCycleDays, PendingGraceDays; public string Etag; }
public class CheckInPeriodResponse { public int Sequence; public DateTimeOffset StartedAt, DueAt, GraceEndsAt;
  public DateTimeOffset? CheckedInAt, OverdueAt, AwaitingCheckAt; }
public interface ICheckInService {
  Task<CheckInStatusResponse> GetMyCheckInAsync(PaginationRequest pagination, CancellationToken ct);
  Task<CheckInStatusResponse> CheckInAsync(CancellationToken ct);              // history = trang 1, pageSize 10
  Task<CheckInStatusResponse> UpdateSettingsAsync(UpdateCheckInSettingsRequest request, CancellationToken ct); // Task 5
}
public sealed class CheckInService : ICheckInService {
  public CheckInService(ICurrentUser currentUser, IVaultRepository vaultRepository,
                        ICheckInRepository checkInRepository, IUnitOfWork unitOfWork, TimeProvider timeProvider);
}
```
Giá trị `Status`: `"NOT_STARTED"` (không có kỳ mở và kế hoạch không bị tạm dừng), `"PAUSED"` (`CheckInRules.IsPaused`), còn lại `CheckInTimeline.EffectiveStatus(...).ToApiString()` → `ACTIVE`/`CHECKIN_OVERDUE`/`AWAITING_CHECK`. `PlanStatus` = `plan.Status.ToApiString()`. Chưa có schedule → `Settings` mặc định 30/7, `Etag = "\"0\""`. Lỗi: `CHECKIN_NOT_ALLOWED` (409, extension `planStatus`), `VAULT_NOT_FOUND` (404, dùng `VaultRules.EnsureOwnedBy`/mẫu của `VaultService.GetMyVaultAsync`), `SESSION_EXPIRED` (`RequireUserId`).

- [ ] **Step 1: Viết test (đỏ)** — `CheckInServiceTests`, `now = 2026-10-14T08:00Z`, kỳ mẫu `start=2026-10-01T08:00Z` (due `2026-10-31T08:00Z`, graceEnds `2026-11-07T08:00Z`):
  - `Get_DraftPlan_ReturnsNotStarted`: `Status=="NOT_STARTED"`, các mốc `null`, `History.Items` rỗng, `Settings.CycleDays==30`, `Etag=="\"0\""`.
  - `Get_ReportsEffectiveStatusEvenIfJobDidNotRun`: plan vẫn `Active` trong DB nhưng `now = due` → `Status=="CHECKIN_OVERDUE"`; `now = graceEnds` → `"AWAITING_CHECK"`; `PlanStatus` vẫn `"ACTIVE"`.
  - `Get_PausedPlan_ReturnsPaused`: plan `DeathCaseOpen` → `"PAUSED"`.
  - `Get_ReturnsHistoryNewestFirst`: 3 kỳ → `Items[0].Sequence==3`, `TotalCount==3`.
  - `Get_NoVault_ThrowsVaultNotFound`; `Get_NotAuthenticated_ThrowsSessionExpired`.
  - `CheckIn_FromEachRunningStatus_ReopensActivePeriod` (`[Theory]` Active/CheckinOverdue/AwaitingCheck): kỳ cũ `CheckedInAt==now && ClosedAt==now`; có đúng 1 kỳ mở mới `Sequence==2`, `StartedAt==now`, `DueAt==now+30d`; `plan.Status==Active`; `schedule.LastCheckInAt==now`; `FakeUnitOfWork.SaveCount==1`.
  - `CheckIn_AppliesPendingSettingsThenClearsThem`: schedule `Pending=(60,14)` → kỳ mới `DueAt==now+60d`, `GraceEndsAt==DueAt+14d`; `schedule.CycleDays==60`, `GraceDays==14`, hai `Pending*==null`.
  - `CheckIn_WithinDuplicateWindow_ReturnsCurrentStateWithoutNewPeriod`: `LastCheckInAt = now−59s` → không tạo kỳ, `SaveCount==0`; `LastCheckInAt = now−60s` → **tạo** kỳ mới.
  - `CheckIn_RunningPlanWithoutOpenPeriod_OpensFirstPeriod`: plan `Active`, không có kỳ → tạo `Sequence==1`.
  - `CheckIn_DraftOrLockedPlan_ThrowsNotAllowed` (`[Theory]` Draft, DeathCaseOpen, HandoverInProgress, Closed, Blocked): `AppException` `Code==CheckInNotAllowed`, `Type==Conflict`, `Extensions["planStatus"]` khớp; không đổi dữ liệu.
  - `CheckIn_AllowedEvenWhenServiceIsGraceReadOnly`: `vault.Entitlement.Status = GraceReadOnly` → vẫn thành công.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~CheckInServiceTests"` — Expected: FAIL (lỗi biên dịch/chưa cài).
- [ ] **Step 3: Cài DTO, `ICheckInService`, `CheckInService`** (stub `UpdateSettingsAsync` ném `NotImplementedException` — Task 5 thay). Gom việc dựng `CheckInStatusResponse` vào một hàm private dùng chung cho Get/CheckIn/Update. Mở kỳ mới trong `CheckInAsync` dùng chung `CheckInTimeline` (cùng công thức `CheckInScheduler`); schedule chưa có thì tạo. Một lần `SaveChangesAsync` ở cuối.
- [ ] **Step 4: Run lại** — Expected: PASS.
- [ ] **Step 5: Checkpoint:** `dotnet build` pass.

---

### Task 5: `CheckInService.UpdateSettingsAsync` (PATCH cấu hình)

**Files:**
- Create: `Application/Dtos/Requests/UpdateCheckInSettingsRequest.cs`
- Modify: `CheckInService.cs` (thay stub)
- Test: `tests/.../CheckIn/CheckInSettingsTests.cs`

**Interfaces — Produces:**
```csharp
public class UpdateCheckInSettingsRequest {
  [Required][MaxLength(64)] public string Etag { get; set; } = string.Empty;
  public int? CycleDays { get; set; }
  public int? GraceDays { get; set; }
}
```
Luật: ít nhất một trong `CycleDays`/`GraceDays` phải có (không thì `ValidationErrors.For("cycleDays", "Cần chọn chu kỳ hoặc thời gian chờ.")`); giá trị sai → `ValidationErrors.For("cycleDays"|"graceDays", ...)`; `Etag` không khớp `Etag.Matches(request.Etag, schedule?.UpdatedAt)` → `AppException(Conflict, ErrorCode.EditConflict)`; kế hoạch `IsSettingsLocked` hoặc kho `Closed` → `VaultNotEditable` với `reason` `PLAN_LOCKED`/`VAULT_CLOSED` (cùng định dạng extension như `VaultRules.EnsureEditable`). Có kỳ mở → ghi `Pending*`; không có kỳ mở → ghi thẳng `CycleDays`/`GraceDays` (xóa `Pending*`). Luôn đặt `schedule.UpdatedAt = now`.

- [ ] **Step 1: Viết test (đỏ)** — `CheckInSettingsTests`:
  - `Update_NoOpenPeriod_AppliesImmediatelyAndCreatesSchedule`: plan `Draft`, chưa có schedule, request `Etag="\"0\""`, 60/14 → schedule `CycleDays==60`, `GraceDays==14`; response `Settings.Etag != "\"0\""`.
  - `Update_WithOpenPeriod_StoresPendingAndKeepsCurrentDeadline`: kỳ mở `DueAt=D` → sau PATCH 90/30: `schedule.CycleDays==30` (cũ), `PendingCycleDays==90`, `PendingGraceDays==30`, **`period.DueAt==D` không đổi**.
  - `Update_OnlyOneField_KeepsOther`: chỉ gửi `GraceDays=14` → `CycleDays` giữ nguyên.
  - `Update_InvalidValues_ThrowsValidation` (`[Theory]` cycle: 0, −30, 31, 45, 120; grace: 0, −7, 8, 21, 60) → `Code==ValidationError`, không đổi dữ liệu.
  - `Update_EmptyBody_ThrowsValidation` (cả hai `null`).
  - `Update_StaleOrMalformedEtag_ThrowsEditConflict` (`"\"123\""`, `"abc"`).
  - `Update_LockedPlan_ThrowsVaultNotEditablePlanLocked` (`DeathCaseOpen`, `HandoverInProgress`, `Closed`); `Update_ClosedVault_ThrowsVaultClosed`.
  - `Update_BlockedPlan_IsAllowed` (`Blocked` không khóa cấu hình).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~CheckInSettingsTests"` — Expected: FAIL.
- [ ] **Step 3: Cài `UpdateSettingsAsync`** theo luật trên; dùng lại hàm dựng response của Task 4.
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~CheckIn"` (toàn bộ test điểm danh đã có) — Expected: PASS.
- [ ] **Step 5: Checkpoint:** `dotnet build` pass.

---

### Task 6: `CheckInJob` (chuyển trạng thái và gửi nhắc)

**Files:**
- Create: `Application/Services/ICheckInJob.cs`, `CheckInJob.cs`
- Test: `tests/.../CheckIn/CheckInJobTests.cs`

**Interfaces — Produces:**
```csharp
public interface ICheckInJob {
  Task<IReadOnlyList<Guid>> FindOpenPeriodIdsAsync(CancellationToken ct);       // = repository.FindOpenPeriodIdsForJobAsync(timeProvider.GetUtcNow(), ct)
  Task ProcessPeriodAsync(Guid periodId, CancellationToken ct);
}
public sealed class CheckInJob : ICheckInJob {
  public CheckInJob(ICheckInRepository repository, ICheckInReminderSender reminderSender,
                    IUnitOfWork unitOfWork, TimeProvider timeProvider);
}
```
Thuật toán `ProcessPeriodAsync` (theo thứ tự): kỳ không tồn tại / đã `ClosedAt` / `plan.Status` ngoài {`Active`,`CheckinOverdue`} → return. (1) `Active` và `now >= DueAt` → `OverdueAt=now`, `plan.Status=CheckinOverdue`, `plan.UpdatedAt=now`. (2) `CheckinOverdue` và `now >= GraceEndsAt` → `AwaitingCheckAt=now`, `plan.Status=AwaitingCheck`. Có đổi thì `SaveChangesAsync`. (3) Nếu `plan.Status` sau đó là `AwaitingCheck` → **không nhắc**, return. (4) `latest = LatestReachedMilestone`; null → return. Mốc cũ hơn `latest`: chưa có dòng → thêm dòng `Skipped`; dòng `Pending` → đổi `Skipped`. Mốc `latest`: chưa có → thêm `Pending`; đã `Sent` → return. `SaveChangesAsync`. (5) Gửi qua `ICheckInReminderSender` tới `plan.Vault.Owner.Email`/`FullName`; thành công → `Status=Sent`, `SentAt=now`, `SaveChangesAsync`; ném exception → để nguyên `Pending` rồi ném lại.

- [ ] **Step 1: Viết test (đỏ)** — `CheckInJobTests` (kỳ `start=2026-10-01T08:00Z`, 30/7, nên `due=2026-10-31T08:00Z`, `graceEnds=2026-11-07T08:00Z`; plan `Active`; sender giả; đồng hồ chỉnh từng test):
  - `Process_BeforeDue_DoesNotChangeStatus`: `now = due−1s` → plan `Active`, `OverdueAt==null`.
  - `Process_AtDue_MovesToOverdueOnceAndSendsGraceStarted`: `now = due` → `plan.Status==CheckinOverdue`, `OverdueAt==due`, `Sent == [GraceStarted]`.
  - `Process_AtGraceEnd_MovesToAwaitingCheckAndStopsReminders`: kế hoạch `CheckinOverdue`, `now = graceEnds` → `AwaitingCheck`, `AwaitingCheckAt==graceEnds`, sender **không** được gọi.
  - `Process_LongDowntime_JumpsToAwaitingCheckInOneRun`: plan `Active`, `now = graceEnds+10d` → `AwaitingCheck`, cả `OverdueAt` và `AwaitingCheckAt` có giá trị, không nhắc nào được gửi.
  - `Process_RunTwice_DoesNotDuplicateWorkOrReminders`: chạy 2 lần liên tiếp cùng `now` → `Reminders.Count` không tăng, `Sent.Count==1`, `AwaitingCheckAt`/`OverdueAt` không đổi.
  - `Process_SendsEachFourMilestoneOnce` : lần lượt `now = due−7d`, `due−1d`, `due`, `graceEnds−2d` (chạy job sau mỗi lần) → `Sent == [DueInSevenDays, DueInOneDay, GraceStarted, GraceTwoDaysLeft]`.
  - `Process_AfterDowntime_SendsOnlyLatestMilestoneAndSkipsOlder`: lần chạy đầu tiên ở `now = due−1d+1h` → `Sent == [DueInOneDay]`, dòng `DueInSevenDays` có `Status==Skipped`.
  - `Process_SenderFails_KeepsPendingAndRetriesWithoutDuplicate`: `FailNext=true`, chạy → ném `InvalidOperationException`, dòng `Pending`, `Sent` rỗng; chạy lại → dòng `Sent`, `Reminders.Count(r => r.Kind==DueInSevenDays)==1`.
  - `Process_LockedPlanOrClosedPeriod_IsIgnored` (`[Theory]` plan `DeathCaseOpen`, `Draft`; và kỳ có `ClosedAt`): không đổi gì, không gửi.
  - `FindOpenPeriodIds_OnlyReturnsPeriodsNeedingWork`: kỳ có `due = now+30d` không nằm trong danh sách; kỳ có `due = now+7d` nằm trong danh sách.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~CheckInJobTests"` — Expected: FAIL.
- [ ] **Step 3: Cài `CheckInJob`** theo thuật toán trên.
- [ ] **Step 4: Run lại** — Expected: PASS.
- [ ] **Step 5: Checkpoint:** `dotnet build` pass.

---

### Task 7: Nối vào hệ thống — job nền, gửi nhắc, DI, controller, cấu hình

**Files:**
- Create: `Infrastructure/Jobs/CheckInJobSettings.cs`, `CheckInMonitor.cs`, `Infrastructure/Notifications/LogCheckInReminderSender.cs`, `Api/Controllers/CheckInsController.cs`
- Modify: `ApplicationConfig.cs` (thêm 3 dòng `AddScoped`: `ICheckInService`, `ICheckInScheduler`, `ICheckInJob`), `InfrastructureConfig.cs` (thêm: bind `CheckInJobSettings` mục `"CheckIn"`, `AddSingleton<ICheckInReminderSender, LogCheckInReminderSender>()`, `AddHostedService<CheckInMonitor>()`), `appsettings.Development.json` (thêm `"CheckIn": { "Enabled": true, "PollSeconds": 60 }`)
- Test: `tests/.../CheckIn/CheckInMonitorTests.cs`

**Interfaces — Produces:**
```csharp
public sealed class CheckInJobSettings { public const string SectionName = "CheckIn"; public bool Enabled { get; set; } = true; public int PollSeconds { get; set; } = 60; }
public sealed class CheckInMonitor : BackgroundService {
  public CheckInMonitor(IServiceScopeFactory scopeFactory, IOptions<CheckInJobSettings> settings, ILogger<CheckInMonitor> logger);
}
// CheckInsController: [Authorize] [Route("api/v1")]
GET   api/v1/check-ins/me                         -> CheckInStatusResponse   ([FromQuery] PaginationRequest)
POST  api/v1/check-ins                            -> CheckInStatusResponse   (200)
PATCH api/v1/vaults/me/check-in-settings          -> CheckInStatusResponse   (body UpdateCheckInSettingsRequest)  // [HttpPatch("~/api/v1/vaults/me/check-in-settings")]
```
- [ ] **Step 1: Viết test (đỏ)** — `CheckInMonitorTests` dùng `ServiceCollection` thật + `FakeCheckInJob : ICheckInJob` (đếm số lần `FindOpenPeriodIdsAsync` và `ProcessPeriodAsync`, trả sẵn 2 Id; Id thứ nhất ném exception):
  - `Monitor_Disabled_NeverCallsJob`: `Enabled=false` → `StartAsync`, đợi 1,5 giây, `StopAsync` → job 0 lần.
  - `Monitor_Enabled_ProcessesEveryPeriodAndSurvivesOneFailure`: `Enabled=true, PollSeconds=1` → trong tối đa 10 giây (vòng chờ có timeout, không `Thread.Sleep` cố định) job được gọi `ProcessPeriodAsync` cho **cả hai** Id dù Id đầu ném lỗi; `StopAsync` kết thúc trong 5 giây.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~CheckInMonitorTests"` — Expected: FAIL (chưa có lớp).
- [ ] **Step 3: Cài `CheckInMonitor`:** `ExecuteAsync` thoát ngay nếu `!Enabled`; vòng lặp `Task.Delay(PollSeconds)` **trước** mỗi lượt (không chạy ngay lúc khởi động); mỗi lượt mở một scope lấy `ICheckInJob.FindOpenPeriodIdsAsync`, rồi **mỗi Id mở scope mới** gọi `ProcessPeriodAsync` trong `try/catch`; log chỉ ghi kiểu exception + periodId (không email, không nội dung). Lỗi ngoài vòng cũng được bắt và log; `OperationCanceledException` khi dừng thì thoát êm.
- [ ] **Step 4: Cài `LogCheckInReminderSender`** (ghi `LogInformation` với kind và mốc giờ, **không** ghi email đầy đủ — dùng `EmailMasker` nếu truy cập được, nếu không chỉ ghi kind) và `CheckInsController` (dùng `ICheckInService`, kiểu trả về `Task<CheckInStatusResponse>` như `VaultsController`; `[ProducesResponseType]` 200/400/401/404/409).
- [ ] **Step 5: Đăng ký DI** (các dòng thêm ở phần Modify) và cấu hình `appsettings.Development.json`.
- [ ] **Step 6: Run** `dotnet build` rồi `dotnet test tests/LegacyVault.IntegrationTests --filter "FullyQualifiedName~CheckIn"` — Expected: build OK; mọi test điểm danh PASS (bỏ qua `[SqlFact]`).
- [ ] **Step 7: Kiểm tra khởi động** — Run: `dotnet test tests/LegacyVault.IntegrationTests --filter "FullyQualifiedName~HealthEndpointTests"` — Expected: PASS (ứng dụng vẫn khởi động được khi có `CheckInMonitor`).
- [ ] **Step 8: Checkpoint:** build pass.

---

### Task 8: Test tích hợp SQL, tài liệu, kiểm chứng toàn bộ

**Files:**
- Create: `tests/.../CheckIn/CheckInFlowTests.cs` (`[SqlFact]`, mẫu `ContainerFlowTests`: `ApiFactory.CreateForTempDatabase()` + `ApiTestHelpers.CreateClientForNewUserAsync`), `http/checkins.http`
- Modify: `docs/api-contract.md` (thêm mục **6.8** mô tả 3 API + bảng mã lỗi dòng `409 CHECKIN_NOT_ALLOWED` với trường phụ `planStatus`), `README.md` (1 dòng ở mục trạng thái: đã có điểm danh; chưa có kích hoạt/Executor), spec `docs/superpowers/specs/2026-10-14-deadman-switch-design.md` §5–6 (cập nhật 4 điều chỉnh ở đầu plan này)

- [ ] **Step 1: Viết `CheckInFlowTests`** (chạy khi có `LEGACYVAULT_TEST_CONNECTION`): (a) `Get_BeforeActivation_ReturnsNotStarted` — tạo kho, `GET /api/v1/check-ins/me` → 200 `NOT_STARTED`; (b) `Settings_ThenCheckIn_FullFlow` — đặt plan `Active` bằng `ExecuteUpdateAsync`, `POST /check-ins` (mở kỳ đầu) → 200 `ACTIVE`; `PATCH` 60/14 → `pendingCycleDays==60`; chờ >60s là không khả thi nên kiểm tra `POST` lần hai (trong 60s) trả cùng `nextCheckInDueAt`; (c) `CheckIn_OnDraftPlan_Returns409` mã `CHECKIN_NOT_ALLOWED`; (d) `PartialUniqueIndex_RejectsSecondOpenPeriod` — chèn trực tiếp hai kỳ mở cùng `PlanId` → `DbUpdateException`; (e) `Unauthenticated_Returns401` mã `SESSION_EXPIRED`.
- [ ] **Step 2: Viết `http/checkins.http`** theo mẫu `http/containers.http` (đăng nhập lấy token, ba request, ghi chú cách đặt plan `Active` bằng SQL khi chưa có chức năng kích hoạt).
- [ ] **Step 3: Cập nhật tài liệu** như danh sách Modify; ví dụ JSON trong `api-contract.md` lấy từ spec §5.
- [ ] **Step 4: Kiểm tra không còn toán tử ba ngôi trong file mới.** Run: dùng Grep mẫu `\?[^?:;]*\s:\s` (và `\? .* : ` ) trên các file `CheckIn*.cs` + `LogCheckInReminderSender.cs` — Expected: không kết quả.
- [ ] **Step 5: Chạy toàn bộ.** Run: `dotnet build` rồi `dotnet test` — Expected: 0 lỗi; mọi test không-SQL PASS; test `[SqlFact]` SKIP (ghi rõ trong báo cáo cuối, kèm tên các test SQL chưa được chạy).
- [ ] **Step 6: Vẽ sơ đồ và hướng dẫn đọc** (sản phẩm cuối theo yêu cầu của người dùng): (1) sơ đồ trạng thái `DRAFT → ACTIVE → CHECKIN_OVERDUE → AWAITING_CHECK` với mũi tên điểm danh; (2) dòng thời gian một kỳ với 4 mốc nhắc; (3) luồng job `CheckInMonitor`. Trình bày kèm giải thích cách đọc từng ký hiệu.
- [ ] **Step 7: Checkpoint:** báo cáo trung thực kết quả build/test; **không commit** trừ khi được yêu cầu.

---

## Self-review

- **Spec coverage:** §3 dữ liệu → Task 1; §4 luật thời gian → Task 2; §5 API → Task 4 (GET/POST), Task 5 (PATCH), Task 7 (controller); §6 job → Task 6, 7; §7 ranh giới (`ICheckInScheduler`, `ICheckInReminderSender`, `AwaitingCheckAt`) → Task 3; §8 file → File Structure; §9 kiểm thử → các task + Task 8; §10 quy ước → Global Constraints; §11 sơ đồ → Task 8 Step 6.
- **Nhất quán tên:** `FindOpenPeriodIdsForJobAsync` (repository, nhận `now`) khác `ICheckInJob.FindOpenPeriodIdsAsync` (job, tự lấy `now`) — chủ ý. `CheckInReminderKind` dùng đúng 4 giá trị ở mọi task.
- **Tỷ lệ:** plan mô tả quyết định và test, không chép thân hàm.
