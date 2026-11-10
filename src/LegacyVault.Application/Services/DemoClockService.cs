using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// Thuật toán tua: lặp "tìm mốc kế tiếp trong (giờ hệ thống, ngày đích) → đặt giờ hệ thống ngay sau mốc → chạy việc nền
/// một lượt". Mốc được tìm lại sau mỗi bước nên mốc mới do việc nền sinh ra (ví dụ sự cố) cũng được xét. Cuối cùng đặt giờ
/// đúng 9:00 sáng giờ Việt Nam của ngày đích và chạy việc nền lần cuối. Chỉ cho một lần tua chạy tại một thời điểm.
/// </remarks>
public sealed class DemoClockService : IDemoClockService
{
    private static readonly SemaphoreSlim TravelGate = new SemaphoreSlim(1, 1);

    private readonly IDemoClock clock;
    private readonly IDemoClockRepository repository;
    private readonly IDemoJobRunner jobRunner;
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;

    public DemoClockService(
        IDemoClock clock,
        IDemoClockRepository repository,
        IDemoJobRunner jobRunner,
        ICurrentUser currentUser,
        IUserRepository users)
    {
        this.clock = clock;
        this.repository = repository;
        this.jobRunner = jobRunner;
        this.currentUser = currentUser;
        this.users = users;
    }

    public Task<DemoClockStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        return Task.FromResult(BuildStatus());
    }

    public async Task<DemoClockTravelResponse> TravelToAsync(DemoClockTravelRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(ct);

        DateOnly date;
        if (!DemoClockRules.TryParseDate(request.TargetDate, out date))
        {
            throw ValidationErrors.For("targetDate", "Ngày phải có dạng yyyy-MM-dd, ví dụ 2026-12-31.");
        }
        DateTimeOffset target = DemoClockRules.TargetInstant(date);
        DateTimeOffset start = clock.RealNow + clock.Offset;
        string? error = DemoClockRules.FindTravelError(start, target);
        if (error != null)
        {
            throw ValidationErrors.For("targetDate", error);
        }

        if (!await TravelGate.WaitAsync(0, ct))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.DemoClockBusy,
                "Đang có một lần tua khác chạy. Hãy chờ nó xong rồi thử lại.");
        }
        try
        {
            DemoClockTravelResponse response = new DemoClockTravelResponse();
            try
            {
                await RunStepsAsync(target, response.Steps, ct);
            }
            finally
            {
                // Lưu độ lệch kể cả khi bước giữa chừng lỗi, để lần khởi động sau vẫn đúng giờ đã tới.
                await repository.SaveOffsetSecondsAsync(
                    (long)clock.Offset.TotalSeconds, clock.RealNow + clock.Offset, CancellationToken.None);
            }
            response.Clock = BuildStatus();
            return response;
        }
        finally
        {
            TravelGate.Release();
        }
    }

    public async Task<DemoClockStatusResponse> ResetAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        clock.SetOffset(TimeSpan.Zero);
        await repository.SaveOffsetSecondsAsync(0, clock.RealNow, ct);
        return BuildStatus();
    }

    private async Task RunStepsAsync(DateTimeOffset target, List<DemoClockStepResponse> steps, CancellationToken ct)
    {
        TimeSpan remindAfter = ExecutorInvitationJob.ReminderAfter;
        TimeSpan escalateAfter = IncidentRules.EscalateAfter;

        int guard = 0;
        while (guard < DemoClockRules.MaxSteps)
        {
            guard++;
            DateTimeOffset now = clock.RealNow + clock.Offset;
            DemoDeadline? next = await FindNextMilestoneAsync(now, target, remindAfter, escalateAfter, ct);
            if (next == null)
            {
                break;
            }
            DateTimeOffset stepTime = next.At + DemoClockRules.AfterMilestone;
            if (stepTime > target)
            {
                stepTime = target;
            }
            steps.Add(await RunStepAsync(stepTime, next.Kind, ct));
        }

        steps.Add(await RunStepAsync(target, DemoClockRules.KindTargetDay, ct));
    }

    private async Task<DemoDeadline?> FindNextMilestoneAsync(
        DateTimeOffset now, DateTimeOffset target, TimeSpan remindAfter, TimeSpan escalateAfter, CancellationToken ct)
    {
        List<DemoOpenPeriod> periods = await repository.ListOpenCheckInPeriodsAsync(ct);
        List<DemoDeadline> candidates = DemoClockRules.CheckInMilestones(periods, now, target);
        List<DemoDeadline> others = await repository.ListUpcomingDeadlinesAsync(now, target, remindAfter, escalateAfter, ct);
        foreach (DemoDeadline other in others)
        {
            if (other.At < target)
            {
                candidates.Add(other);
            }
        }
        return DemoClockRules.PickEarliest(candidates);
    }

    private async Task<DemoClockStepResponse> RunStepAsync(DateTimeOffset stepTime, string kind, CancellationToken ct)
    {
        clock.SetOffset(stepTime - clock.RealNow);
        DemoJobPassResult pass = await jobRunner.RunAllJobsOnceAsync(ct);

        DemoClockStepResponse step = new DemoClockStepResponse();
        step.At = stepTime;
        step.Kind = kind;
        step.ItemsProcessed = pass.ItemsProcessed;
        step.EmailsSent = pass.EmailsSent;
        step.Failures = pass.Failures;
        return step;
    }

    private DemoClockStatusResponse BuildStatus()
    {
        DemoClockStatusResponse status = new DemoClockStatusResponse();
        status.RealNowUtc = clock.RealNow;
        status.NowUtc = clock.RealNow + clock.Offset;
        status.OffsetSeconds = (long)clock.Offset.TotalSeconds;
        status.IsSimulated = clock.Offset != TimeSpan.Zero;
        return status;
    }

    /// <summary>Quyền Admin đọc từ DB mỗi lần gọi; không phải Admin đang hoạt động thì 404 như các công cụ dev khác.</summary>
    private async Task<Guid> RequireAdminAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Role != UserRole.Admin || user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }
        return userId;
    }
}
