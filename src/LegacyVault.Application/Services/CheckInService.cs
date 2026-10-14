using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// Điểm danh KHÔNG gọi VaultRules.EnsureEditable: SRS cho phép điểm danh cả khi dịch vụ đang ân hạn
/// (OPLAN-02), còn EnsureEditable chặn mọi thao tác "thêm" ở chế độ chỉ đọc.
/// </remarks>
public sealed class CheckInService : ICheckInService
{
    private const string StatusNotStarted = "NOT_STARTED";
    private const string StatusPaused = "PAUSED";

    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly ICheckInRepository checkInRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public CheckInService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        ICheckInRepository checkInRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.checkInRepository = checkInRepository;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<CheckInStatusResponse> GetMyCheckInAsync(PaginationRequest pagination, CancellationToken ct)
    {
        Vault vault = await LoadMyVaultAsync(ct);
        return await BuildResponseAsync(vault.Plan, pagination, ct);
    }

    public async Task<CheckInStatusResponse> CheckInAsync(CancellationToken ct)
    {
        Vault vault = await LoadMyVaultAsync(ct);
        HandoverPlan plan = vault.Plan;
        if (!CheckInRules.IsRunning(plan.Status))
        {
            throw NotAllowed(plan.Status);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        CheckInSchedule? schedule = await checkInRepository.FindScheduleByPlanIdAsync(plan.Id, ct);
        if (schedule != null && schedule.LastCheckInAt != null
            && now - schedule.LastCheckInAt.Value < CheckInRules.DuplicateWindow)
        {
            // Bấm lặp: không mở kỳ mới, chỉ trả trạng thái hiện tại.
            return await BuildResponseAsync(plan, new PaginationRequest(), ct);
        }
        if (schedule == null)
        {
            schedule = new CheckInSchedule();
            schedule.PlanId = plan.Id;
            schedule.CreatedAt = now;
            checkInRepository.AddSchedule(schedule);
        }

        CheckInPeriod? openPeriod = await checkInRepository.FindOpenPeriodByPlanIdAsync(plan.Id, ct);
        if (openPeriod != null)
        {
            openPeriod.CheckedInAt = now;
            openPeriod.ClosedAt = now;
        }

        ApplyPendingSettings(schedule, now);
        schedule.LastCheckInAt = now;

        int lastSequence = await checkInRepository.GetMaxSequenceAsync(plan.Id, ct);
        CheckInPeriod next = new CheckInPeriod();
        next.PlanId = plan.Id;
        next.Sequence = lastSequence + 1;
        next.StartedAt = now;
        next.DueAt = CheckInTimeline.ComputeDueAt(now, schedule.CycleDays);
        next.GraceEndsAt = CheckInTimeline.ComputeGraceEndsAt(next.DueAt, schedule.GraceDays);
        checkInRepository.AddPeriod(next);

        plan.Status = PlanStatus.Active;
        plan.UpdatedAt = now;

        // Một lần lưu cho cả kỳ cũ, kỳ mới, lịch và kế hoạch (STATE-02). Hai yêu cầu song song:
        // RowVersion của kỳ cũ lệch nên bên thua nhận 409 EDIT_CONFLICT.
        await unitOfWork.SaveChangesAsync(ct);
        return await BuildResponseAsync(plan, new PaginationRequest(), ct);
    }

    public async Task<CheckInStatusResponse> UpdateSettingsAsync(
        UpdateCheckInSettingsRequest request, CancellationToken ct)
    {
        Vault vault = await LoadMyVaultAsync(ct);
        HandoverPlan plan = vault.Plan;

        ValidateSettings(request);
        VaultNotEditableReason? blockedReason = GetSettingsBlockedReason(vault);
        if (blockedReason != null)
        {
            Dictionary<string, object?> extensions = new Dictionary<string, object?>();
            extensions["reason"] = blockedReason.Value.ToApiString();
            throw new AppException(AppErrorType.Conflict, ErrorCode.VaultNotEditable,
                "Kho hiện không cho phép chỉnh sửa.", extensions);
        }

        CheckInSchedule? schedule = await checkInRepository.FindScheduleByPlanIdAsync(plan.Id, ct);
        DateTimeOffset? currentUpdatedAt = null;
        if (schedule != null)
        {
            currentUpdatedAt = schedule.UpdatedAt;
        }
        if (!Etag.Matches(request.Etag, currentUpdatedAt))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Dữ liệu vừa được sửa ở nơi khác. Vui lòng tải lại rồi sửa tiếp.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (schedule == null)
        {
            schedule = new CheckInSchedule();
            schedule.PlanId = plan.Id;
            schedule.CreatedAt = now;
            checkInRepository.AddSchedule(schedule);
        }

        CheckInPeriod? openPeriod = await checkInRepository.FindOpenPeriodByPlanIdAsync(plan.Id, ct);
        if (openPeriod != null)
        {
            // DMS-01: kỳ đang chạy giữ nguyên hạn; lựa chọn mới chỉ áp dụng từ kỳ sau.
            if (request.CycleDays != null)
            {
                schedule.PendingCycleDays = request.CycleDays;
            }
            if (request.GraceDays != null)
            {
                schedule.PendingGraceDays = request.GraceDays;
            }
        }
        else
        {
            if (request.CycleDays != null)
            {
                schedule.CycleDays = request.CycleDays.Value;
            }
            if (request.GraceDays != null)
            {
                schedule.GraceDays = request.GraceDays.Value;
            }
            schedule.PendingCycleDays = null;
            schedule.PendingGraceDays = null;
        }
        schedule.UpdatedAt = now;

        await unitOfWork.SaveChangesAsync(ct);
        return await BuildResponseAsync(plan, new PaginationRequest(), ct);
    }

    private static void ValidateSettings(UpdateCheckInSettingsRequest request)
    {
        if (request.CycleDays == null && request.GraceDays == null)
        {
            throw ValidationErrors.For("cycleDays", "Cần chọn chu kỳ hoặc thời gian chờ.");
        }
        if (request.CycleDays != null && !CheckInRules.IsAllowedCycleDays(request.CycleDays.Value))
        {
            throw ValidationErrors.For("cycleDays", "Chu kỳ điểm danh chỉ nhận 30, 60 hoặc 90 ngày.");
        }
        if (request.GraceDays != null && !CheckInRules.IsAllowedGraceDays(request.GraceDays.Value))
        {
            throw ValidationErrors.For("graceDays", "Thời gian chờ chỉ nhận 7, 14 hoặc 30 ngày.");
        }
    }

    /// <summary>Đổi cấu hình bị chặn khi kho đã đóng hoặc kế hoạch đã khóa; khác VaultRules vì không chặn khi dịch vụ chỉ đọc.</summary>
    private static VaultNotEditableReason? GetSettingsBlockedReason(Vault vault)
    {
        if (vault.Status == VaultStatus.Closed)
        {
            return VaultNotEditableReason.VaultClosed;
        }
        if (CheckInRules.IsSettingsLocked(vault.Plan.Status))
        {
            return VaultNotEditableReason.PlanLocked;
        }
        return null;
    }

    /// <summary>DMS-01: lựa chọn chờ áp dụng được dùng khi mở kỳ mới, rồi xóa.</summary>
    private static void ApplyPendingSettings(CheckInSchedule schedule, DateTimeOffset now)
    {
        bool changed = false;
        if (schedule.PendingCycleDays != null)
        {
            schedule.CycleDays = schedule.PendingCycleDays.Value;
            schedule.PendingCycleDays = null;
            changed = true;
        }
        if (schedule.PendingGraceDays != null)
        {
            schedule.GraceDays = schedule.PendingGraceDays.Value;
            schedule.PendingGraceDays = null;
            changed = true;
        }
        if (changed)
        {
            schedule.UpdatedAt = now;
        }
    }

    private async Task<Vault> LoadMyVaultAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByOwnerIdAsync(userId, ct);
        if (vault == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }
        return vault;
    }

    private async Task<CheckInStatusResponse> BuildResponseAsync(
        HandoverPlan plan, PaginationRequest pagination, CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        CheckInSchedule? schedule = await checkInRepository.FindScheduleByPlanIdAsync(plan.Id, ct);
        CheckInPeriod? openPeriod = await checkInRepository.FindOpenPeriodByPlanIdAsync(plan.Id, ct);
        var page = await checkInRepository.FindPeriodPageByPlanIdAsync(
            plan.Id, pagination.PageIndex, pagination.PageSize, ct);

        CheckInStatusResponse response = new CheckInStatusResponse();
        response.PlanStatus = plan.Status.ToApiString();
        response.ServerTime = now;
        response.Settings = BuildSettings(schedule);

        List<CheckInPeriodResponse> items = new List<CheckInPeriodResponse>();
        foreach (CheckInPeriod period in page.Items)
        {
            items.Add(ToPeriodResponse(period));
        }
        response.History = items.ToPaginatedList(page.TotalCount, pagination);

        if (schedule != null)
        {
            response.LastCheckInAt = schedule.LastCheckInAt;
        }

        if (CheckInRules.IsPaused(plan.Status))
        {
            response.Status = StatusPaused;
        }
        else if (!CheckInRules.IsRunning(plan.Status) || openPeriod == null)
        {
            response.Status = StatusNotStarted;
        }
        else
        {
            response.Status = CheckInTimeline.EffectiveStatus(openPeriod.DueAt, openPeriod.GraceEndsAt, now).ToApiString();
            response.NextCheckInDueAt = openPeriod.DueAt;
            response.GraceEndsAt = openPeriod.GraceEndsAt;
        }
        return response;
    }

    private static CheckInSettingsResponse BuildSettings(CheckInSchedule? schedule)
    {
        CheckInSettingsResponse settings = new CheckInSettingsResponse();
        if (schedule == null)
        {
            settings.CycleDays = CheckInRules.DefaultCycleDays;
            settings.GraceDays = CheckInRules.DefaultGraceDays;
            settings.Etag = Etag.From(null);
            return settings;
        }
        settings.CycleDays = schedule.CycleDays;
        settings.GraceDays = schedule.GraceDays;
        settings.PendingCycleDays = schedule.PendingCycleDays;
        settings.PendingGraceDays = schedule.PendingGraceDays;
        settings.Etag = Etag.From(schedule.UpdatedAt);
        return settings;
    }

    private static CheckInPeriodResponse ToPeriodResponse(CheckInPeriod period)
    {
        CheckInPeriodResponse response = new CheckInPeriodResponse();
        response.Sequence = period.Sequence;
        response.StartedAt = period.StartedAt;
        response.DueAt = period.DueAt;
        response.GraceEndsAt = period.GraceEndsAt;
        response.CheckedInAt = period.CheckedInAt;
        response.OverdueAt = period.OverdueAt;
        response.AwaitingCheckAt = period.AwaitingCheckAt;
        return response;
    }

    private static AppException NotAllowed(PlanStatus planStatus)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["planStatus"] = planStatus.ToApiString();
        return new AppException(AppErrorType.Conflict, ErrorCode.CheckinNotAllowed,
            "Kế hoạch hiện không cho phép điểm danh.", extensions);
    }
}
