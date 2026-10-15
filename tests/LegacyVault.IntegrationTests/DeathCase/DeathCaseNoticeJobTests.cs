using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.DeathCases;
using LegacyVault.Infrastructure.Notifications;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Job gửi thông báo Owner theo outbox (DEATH-08, DEATH-11, TIME-02, D3). Dùng fake, không cần SQL.</summary>
public sealed class DeathCaseNoticeJobTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
    private const string OwnerCitizenId = "012345678901";

    private readonly FakeDeathCaseRepository repository = new FakeDeathCaseRepository();
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeDeathCaseNoticeSender sender = new FakeDeathCaseNoticeSender();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly User owner;
    private readonly User executor;

    public DeathCaseNoticeJobTests()
    {
        owner = new User();
        owner.Email = "owner@test.local";
        owner.FullName = "Nguyễn Văn Chủ";
        users.Users.Add(owner);

        executor = new User();
        executor.Email = "executor@test.local";
        executor.FullName = "Trần Thị Thực Thi";
        users.Users.Add(executor);
    }

    [Fact]
    public async Task Death08_DeliveredNoticeStartsSeventyTwoHours()
    {
        // Chuẩn bị
        DeathCase deathCase = AddCase(notifiedVersionNo: 1);
        DeathCaseNotice notice = AddNotice(deathCase, versionNo: 1, attemptCount: 0);

        // Hành động
        DeathCaseNoticeStatus? status = await CreateJob().ProcessNoticeAsync(notice.Id, CancellationToken.None);

        // Kiểm tra
        Assert.Equal(DeathCaseNoticeStatus.Delivered, status);
        Assert.Equal(DeathCaseNoticeStatus.Delivered, notice.Status);
        Assert.Equal(Now, notice.DeliveredAt);
        Assert.Equal(1, notice.AttemptCount);
        Assert.Equal(Now, deathCase.ObjectionStartsAt);
        Assert.Equal(Now.AddHours(72), deathCase.ObjectionEndsAt);
        Assert.Single(sender.Sent);
        Assert.Equal(owner.Email, sender.Sent[0].ToEmail);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Death08_FailedSendKeepsClockStopped()
    {
        // Chuẩn bị
        DeathCase deathCase = AddCase(notifiedVersionNo: 1);
        DeathCaseNotice notice = AddNotice(deathCase, versionNo: 1, attemptCount: 0);
        sender.Fail = true;

        // Hành động
        DeathCaseNoticeStatus? status = await CreateJob().ProcessNoticeAsync(notice.Id, CancellationToken.None);

        // Kiểm tra
        Assert.Equal(DeathCaseNoticeStatus.Pending, status);
        Assert.Equal(1, notice.AttemptCount);
        Assert.Equal(Now, notice.LastAttemptAt);
        Assert.Null(notice.DeliveredAt);
        Assert.Null(deathCase.ObjectionStartsAt);
        Assert.Null(deathCase.ObjectionEndsAt);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Death08_FifthFailureMarksNoticeFailed()
    {
        // Chuẩn bị: đã lỗi 4 lần
        DeathCase deathCase = AddCase(notifiedVersionNo: 1);
        DeathCaseNotice notice = AddNotice(deathCase, versionNo: 1, attemptCount: 4);
        sender.Fail = true;

        // Hành động
        DeathCaseNoticeStatus? status = await CreateJob().ProcessNoticeAsync(notice.Id, CancellationToken.None);
        List<Guid> pendingAfter = await CreateJob().FindPendingNoticeIdsAsync(CancellationToken.None);

        // Kiểm tra
        Assert.Equal(DeathCaseNoticeStatus.Failed, status);
        Assert.Equal(DeathCaseNoticeStatus.Failed, notice.Status);
        Assert.Equal(5, notice.AttemptCount);
        Assert.Null(deathCase.ObjectionStartsAt);
        Assert.Empty(pendingAfter);
    }

    [Fact]
    public async Task Time02_DeliveredNoticeIsNotSentAgain()
    {
        // Chuẩn bị
        DeathCase deathCase = AddCase(notifiedVersionNo: 1);
        DeathCaseNotice notice = AddNotice(deathCase, versionNo: 1, attemptCount: 1);
        notice.Status = DeathCaseNoticeStatus.Delivered;
        notice.DeliveredAt = Now.AddHours(-1);

        // Hành động
        DeathCaseNoticeStatus? status = await CreateJob().ProcessNoticeAsync(notice.Id, CancellationToken.None);

        // Kiểm tra
        Assert.Null(status);
        Assert.Equal(0, sender.CallCount);
        Assert.Equal(1, notice.AttemptCount);
        Assert.Equal(Now.AddHours(-1), notice.DeliveredAt);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Death11_NoticeOfOlderVersionDoesNotStartClock()
    {
        // Chuẩn bị: bản 2 đổi trọng yếu nên NotifiedVersionNo = 2; thông báo cũ của bản 1 vẫn Pending
        DeathCase deathCase = AddCase(notifiedVersionNo: 2);
        DeathCaseNotice oldNotice = AddNotice(deathCase, versionNo: 1, attemptCount: 0);

        // Hành động
        DeathCaseNoticeStatus? status = await CreateJob().ProcessNoticeAsync(oldNotice.Id, CancellationToken.None);

        // Kiểm tra
        Assert.Equal(DeathCaseNoticeStatus.Delivered, status);
        Assert.Null(deathCase.ObjectionStartsAt);
        Assert.Null(deathCase.ObjectionEndsAt);
    }

    [Fact]
    public async Task Ops04_EmailHasNoCitizenIdOrExecutorName()
    {
        // Chuẩn bị
        DeathCase deathCase = AddCase(notifiedVersionNo: 1);
        DeathCaseNotice notice = AddNotice(deathCase, versionNo: 1, attemptCount: 0);

        // Hành động
        await CreateJob().ProcessNoticeAsync(notice.Id, CancellationToken.None);
        DeathCaseOwnerNotice message = Assert.Single(sender.Sent);
        EmailContent email = DeathCaseNoticeEmail.Build(
            message.FullName, message.CaseCode, message.ExpectedObjectionEndsAt, "http://localhost:5173/login");

        // Kiểm tra: chỉ có tên Owner, mã hồ sơ rút gọn và hạn theo giờ VN (03:00 UTC + 72 giờ = 10:00 ngày 18/10)
        string all = email.Subject + email.TextBody + email.HtmlBody;
        Assert.DoesNotContain(OwnerCitizenId, all);
        Assert.DoesNotContain(executor.FullName, all);
        Assert.DoesNotContain(executor.Email, all);
        Assert.Contains(owner.FullName, email.TextBody);
        Assert.Contains(message.CaseCode, email.TextBody);
        Assert.Contains("10:00 ngày 18/10/2026", email.TextBody);
        Assert.Equal(8, message.CaseCode.Length);
    }

    [Fact]
    public async Task Assign04_DeliveredNoticeExtendsActiveAssignmentDueAt()
    {
        // Chuẩn bị: một phân công đang hiệu lực hạn Now + 40 giờ, một phân công cũ đã kết thúc
        DeathCase deathCase = AddCase(notifiedVersionNo: 1);
        DeathCaseNotice notice = AddNotice(deathCase, versionNo: 1, attemptCount: 0);
        DeathCaseAssignment active = AddAssignment(deathCase, Now.AddHours(40), null);
        DeathCaseAssignment ended = AddAssignment(deathCase, Now.AddHours(-5), Now.AddHours(-1));

        // Hành động
        await CreateJob().ProcessNoticeAsync(notice.Id, CancellationToken.None);

        // Kiểm tra: hạn dời tới hết phản đối (Now + 72 giờ) + 48 giờ; dòng đã kết thúc giữ nguyên
        Assert.Equal(Now.AddHours(72 + 48), active.DueAt);
        Assert.Equal(Now.AddHours(-5), ended.DueAt);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Assign04_NoticeOfOlderVersionDoesNotTouchDueAt()
    {
        // Chuẩn bị: bản 2 đã đổi trọng yếu (NotifiedVersionNo = 2), thông báo cũ của bản 1 mới được giao
        DeathCase deathCase = AddCase(notifiedVersionNo: 2);
        DeathCaseNotice oldNotice = AddNotice(deathCase, versionNo: 1, attemptCount: 0);
        DeathCaseAssignment active = AddAssignment(deathCase, Now.AddHours(40), null);

        // Hành động
        await CreateJob().ProcessNoticeAsync(oldNotice.Id, CancellationToken.None);

        // Kiểm tra: đồng hồ 72 giờ không chạy nên hạn Verifier không đổi
        Assert.Equal(Now.AddHours(40), active.DueAt);
    }

    private DeathCaseNoticeJob CreateJob()
    {
        return new DeathCaseNoticeJob(repository, users, sender, unitOfWork, new FixedTimeProvider(Now));
    }

    private DeathCase AddCase(int notifiedVersionNo)
    {
        DeathCase deathCase = new DeathCase();
        deathCase.VaultId = Guid.NewGuid();
        deathCase.PlanId = Guid.NewGuid();
        deathCase.Status = DeathCaseStatus.UnderReview;
        deathCase.CurrentVersionNo = notifiedVersionNo;
        deathCase.ReviewVersionNo = notifiedVersionNo;
        deathCase.NotifiedVersionNo = notifiedVersionNo;
        deathCase.OpenedByUserId = executor.Id;
        deathCase.CreatedAt = Now.AddHours(-2);
        deathCase.FirstSubmittedAt = Now.AddHours(-1);

        DeathCaseVersion version = new DeathCaseVersion();
        version.CaseId = deathCase.Id;
        version.VersionNo = 1;
        version.OwnerCitizenIdLast4 = "8901";
        version.CreatedByUserId = executor.Id;
        version.SubmittedByUserId = executor.Id;
        version.SubmittedAt = Now.AddHours(-1);
        deathCase.Versions.Add(version);

        repository.Cases.Add(deathCase);
        return deathCase;
    }

    private DeathCaseAssignment AddAssignment(DeathCase deathCase, DateTimeOffset dueAt, DateTimeOffset? endedAt)
    {
        DeathCaseAssignment assignment = new DeathCaseAssignment();
        assignment.CaseId = deathCase.Id;
        assignment.VerifierUserId = Guid.NewGuid();
        assignment.AssignedAt = Now.AddHours(-8);
        assignment.DueAt = dueAt;
        assignment.EndedAt = endedAt;
        repository.Assignments.Add(assignment);
        deathCase.Assignments.Add(assignment);
        return assignment;
    }

    private DeathCaseNotice AddNotice(DeathCase deathCase, int versionNo, int attemptCount)
    {
        DeathCaseNotice notice = new DeathCaseNotice();
        notice.CaseId = deathCase.Id;
        notice.VersionNo = versionNo;
        notice.Channel = DeathCaseNoticeChannel.Email;
        notice.RecipientUserId = owner.Id;
        notice.Status = DeathCaseNoticeStatus.Pending;
        notice.AttemptCount = attemptCount;
        notice.CreatedAt = Now.AddHours(-1);
        repository.Notices.Add(notice);
        deathCase.Notices.Add(notice);
        return notice;
    }
}
