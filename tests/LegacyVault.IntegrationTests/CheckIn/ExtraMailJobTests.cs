using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

/// <summary>Thư bổ sung do Admin đặt: đúng ngày, chỉ một lần mỗi kỳ, bỏ qua khi luật tạo muộn, không gửi khi kế hoạch đã khóa.</summary>
public sealed class ExtraMailJobTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);

    private sealed class FakeExtraMailRepository : IExtraMailRepository
    {
        public List<ExtraMailRule> Rules { get; } = new List<ExtraMailRule>();

        public List<CheckInPeriod> Periods { get; } = new List<CheckInPeriod>();

        public List<ExtraMailSend> Sends { get; } = new List<ExtraMailSend>();

        public Task<List<ExtraMailRule>> ListAsync(CancellationToken ct) => Task.FromResult(Rules.ToList());

        public Task<List<ExtraMailRule>> ListEnabledAsync(CancellationToken ct) =>
            Task.FromResult(Rules.Where(r => r.Enabled).ToList());

        public Task<ExtraMailRule?> FindAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Rules.FirstOrDefault(r => r.Id == id));

        public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(Rules.Count);

        public void Add(ExtraMailRule rule) => Rules.Add(rule);

        public void Remove(ExtraMailRule rule) => Rules.Remove(rule);

        public Task<IReadOnlyList<Guid>> FindOpenPeriodIdsAsync(DateTimeOffset horizon, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(Periods.Where(p => p.DueAt <= horizon).Select(p => p.Id).ToList());

        public Task<CheckInPeriod?> FindPeriodAsync(Guid periodId, CancellationToken ct) =>
            Task.FromResult(Periods.FirstOrDefault(p => p.Id == periodId));

        public Task<List<Guid>> ListHandledRuleIdsAsync(Guid periodId, CancellationToken ct) =>
            Task.FromResult(Sends.Where(s => s.PeriodId == periodId).Select(s => s.RuleId).ToList());

        public void AddSend(ExtraMailSend send) => Sends.Add(send);
    }

    private sealed class FakeExtraMailSender : IExtraMailSender
    {
        public List<ExtraMailMessage> Sent { get; } = new List<ExtraMailMessage>();

        public bool Fail { get; set; }

        public Task SendAsync(ExtraMailMessage message, CancellationToken ct)
        {
            if (Fail)
            {
                throw new InvalidOperationException("SMTP lỗi");
            }
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private readonly FakeExtraMailRepository repository = new FakeExtraMailRepository();
    private readonly FakeExtraMailSender sender = new FakeExtraMailSender();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(Start);
    private readonly ExtraMailJob job;

    public ExtraMailJobTests()
    {
        job = new ExtraMailJob(repository, sender, new FakeUnitOfWork(), clock);
    }

    private CheckInPeriod SeedPeriod(PlanStatus status = PlanStatus.Active)
    {
        Vault vault = CheckInTestData.CreateVault(Guid.NewGuid(), status);
        CheckInPeriod period = CheckInTestData.CreateOpenPeriod(vault.Plan, 1, Start, 30);
        repository.Periods.Add(period);
        return period;
    }

    private ExtraMailRule AddRule(int daysFromDue, bool enabled = true)
    {
        ExtraMailRule rule = new ExtraMailRule();
        rule.Name = "Nhắc thêm";
        rule.Enabled = enabled;
        rule.DaysFromDue = daysFromDue;
        rule.Subject = "Chào {TEN_NGUOI_NHAN}";
        rule.Title = "Tiêu đề";
        rule.Body = "Hạn của bạn là {HAN_DIEM_DANH}.";
        rule.ButtonText = "Điểm danh ngay";
        rule.Note = "Ghi chú";
        repository.Rules.Add(rule);
        return rule;
    }

    private async Task RunAtAsync(CheckInPeriod period, DateTimeOffset now)
    {
        clock.Now = now;
        await job.ProcessPeriodAsync(period.Id, CancellationToken.None);
    }

    [Fact]
    public async Task Process_BeforeTheRuleDay_SendsNothing()
    {
        CheckInPeriod period = SeedPeriod();
        AddRule(-3);

        await RunAtAsync(period, Due.AddDays(-3).AddMinutes(-1));

        Assert.Empty(sender.Sent);
        Assert.Empty(repository.Sends);
    }

    [Fact]
    public async Task Process_OnTheRuleDay_SendsOnceWithTokensFilled()
    {
        CheckInPeriod period = SeedPeriod();
        ExtraMailRule rule = AddRule(-3);

        await RunAtAsync(period, Due.AddDays(-3).AddHours(1));
        await RunAtAsync(period, Due.AddDays(-3).AddHours(2));

        ExtraMailMessage message = Assert.Single(sender.Sent);
        Assert.Equal("Chào Chủ kho thử", message.Subject);
        Assert.Contains("ngày 31/10/2026", message.Body);
        ExtraMailSend send = Assert.Single(repository.Sends);
        Assert.True(send.Sent);
        Assert.Equal(rule.Id, send.RuleId);
    }

    [Fact]
    public async Task Process_AfterDue_SendsLateMail()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.CheckinOverdue);
        AddRule(2);

        await RunAtAsync(period, Due.AddDays(2).AddHours(1));

        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task Process_DisabledRule_SendsNothing()
    {
        CheckInPeriod period = SeedPeriod();
        AddRule(0, enabled: false);

        await RunAtAsync(period, Due.AddHours(1));

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Process_RuleCreatedLongAfterItsDay_IsSkippedNotSentLate()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.CheckinOverdue);
        AddRule(-5);

        await RunAtAsync(period, Due.AddDays(1));

        Assert.Empty(sender.Sent);
        ExtraMailSend send = Assert.Single(repository.Sends);
        Assert.False(send.Sent);
    }

    [Fact]
    public async Task Process_SendFails_DoesNotMarkAndRetriesNextRound()
    {
        CheckInPeriod period = SeedPeriod();
        AddRule(0);
        sender.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAtAsync(period, Due.AddHours(1)));
        Assert.Empty(repository.Sends);

        sender.Fail = false;
        await RunAtAsync(period, Due.AddHours(2));

        Assert.Single(sender.Sent);
        Assert.Single(repository.Sends);
    }

    [Fact]
    public async Task Process_PlanAlreadyAwaitingCheck_SendsNothing()
    {
        CheckInPeriod period = SeedPeriod(PlanStatus.AwaitingCheck);
        AddRule(1);

        await RunAtAsync(period, Due.AddDays(1).AddHours(1));

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task FindPeriodIds_NoEnabledRule_ReturnsNothing()
    {
        SeedPeriod();
        AddRule(0, enabled: false);

        IReadOnlyList<Guid> ids = await job.FindPeriodIdsAsync(CancellationToken.None);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task FindPeriodIds_RuleSixtyDaysBefore_ReachesPeriodsFarFromDue()
    {
        CheckInPeriod period = SeedPeriod();
        AddRule(-60);
        clock.Now = Due.AddDays(-45);

        IReadOnlyList<Guid> ids = await job.FindPeriodIdsAsync(CancellationToken.None);

        Assert.Equal(period.Id, Assert.Single(ids));
    }
}
