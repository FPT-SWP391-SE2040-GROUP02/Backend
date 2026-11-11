using System.Globalization;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Gửi thư bổ sung do Admin đặt cho Chủ kho, ngoài các thư nhắc có sẵn. Mỗi luật gửi tối đa một lần cho mỗi kỳ điểm danh,
/// vào ngày (hạn điểm danh + DaysFromDue). Chạy cùng nhịp với job điểm danh.
/// </summary>
public interface IExtraMailJob
{
    /// <summary>Id các kỳ cần xem xét lúc này; rỗng nếu chưa có luật nào đang bật.</summary>
    Task<IReadOnlyList<Guid>> FindPeriodIdsAsync(CancellationToken ct);

    /// <summary>Gửi các thư bổ sung đã tới ngày của một kỳ. Gửi lỗi thì ném exception và vòng sau thử lại.</summary>
    Task ProcessPeriodAsync(Guid periodId, CancellationToken ct);
}

public sealed class ExtraMailJob : IExtraMailJob
{
    /// <summary>Thư đã quá hạn gửi quá lâu (do luật tạo muộn) thì bỏ qua, không gửi bù.</summary>
    public static readonly TimeSpan LateTolerance = TimeSpan.FromDays(2);

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly IExtraMailRepository repository;
    private readonly IExtraMailSender sender;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public ExtraMailJob(
        IExtraMailRepository repository, IExtraMailSender sender, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    {
        this.repository = repository;
        this.sender = sender;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<Guid>> FindPeriodIdsAsync(CancellationToken ct)
    {
        List<ExtraMailRule> rules = await repository.ListEnabledAsync(ct);
        if (rules.Count == 0)
        {
            return new List<Guid>();
        }
        // Luật trước hạn xa nhất quyết định kỳ nào đã tới lúc xem xét.
        int earliest = rules.Min(r => r.DaysFromDue);
        DateTimeOffset horizon = timeProvider.GetUtcNow() + TimeSpan.FromDays(Math.Max(0, -earliest));
        return await repository.FindOpenPeriodIdsAsync(horizon, ct);
    }

    public async Task ProcessPeriodAsync(Guid periodId, CancellationToken ct)
    {
        CheckInPeriod? period = await repository.FindPeriodAsync(periodId, ct);
        if (period == null || period.ClosedAt != null)
        {
            return;
        }
        PlanStatus status = period.Plan.Status;
        if (status != PlanStatus.Active && status != PlanStatus.CheckinOverdue)
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        List<ExtraMailRule> rules = await repository.ListEnabledAsync(ct);
        List<Guid> handled = await repository.ListHandledRuleIdsAsync(periodId, ct);
        User owner = period.Plan.Vault.Owner;

        foreach (ExtraMailRule rule in rules)
        {
            if (handled.Contains(rule.Id))
            {
                continue;
            }
            DateTimeOffset sendAt = period.DueAt + TimeSpan.FromDays(rule.DaysFromDue);
            if (now < sendAt)
            {
                continue;
            }
            if (now - sendAt > LateTolerance)
            {
                Mark(rule.Id, periodId, false, now);
                await unitOfWork.SaveChangesAsync(ct);
                continue;
            }

            await sender.SendAsync(Build(rule, owner, period), ct);
            Mark(rule.Id, periodId, true, now);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private void Mark(Guid ruleId, Guid periodId, bool sent, DateTimeOffset now)
    {
        ExtraMailSend send = new ExtraMailSend();
        send.RuleId = ruleId;
        send.PeriodId = periodId;
        send.Sent = sent;
        send.CreatedAt = now;
        repository.AddSend(send);
    }

    private static ExtraMailMessage Build(ExtraMailRule rule, User owner, CheckInPeriod period)
    {
        Dictionary<string, string> tokens = new Dictionary<string, string>();
        tokens["TEN_NGUOI_NHAN"] = owner.FullName;
        tokens["HAN_DIEM_DANH"] = FormatVietnamTime(period.DueAt);
        tokens["HET_THOI_GIAN_CHO"] = FormatVietnamTime(period.GraceEndsAt);

        ExtraMailMessage message = new ExtraMailMessage();
        message.ToEmail = owner.Email;
        message.FullName = owner.FullName;
        message.Subject = EmailTemplateOverrides.Fill(rule.Subject, tokens);
        message.Title = EmailTemplateOverrides.Fill(rule.Title, tokens);
        message.Body = EmailTemplateOverrides.Fill(rule.Body, tokens);
        message.ButtonText = EmailTemplateOverrides.Fill(rule.ButtonText, tokens);
        message.Note = EmailTemplateOverrides.Fill(rule.Note, tokens);
        return message;
    }

    private static string FormatVietnamTime(DateTimeOffset value)
    {
        return value.ToOffset(VietnamOffset).ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);
    }
}
