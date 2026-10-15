using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Luật thuần Verifier xét hồ sơ chứng tử (không cần DB).</summary>
public sealed class DeathCaseReviewRulesTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Hồ sơ đang xét bản 1, Owner đã được báo, hết 72 giờ phản đối, không có cờ: duyệt được.</summary>
    private static DeathCase NewReadyCase()
    {
        DeathCase deathCase = new DeathCase();
        deathCase.Status = DeathCaseStatus.UnderReview;
        deathCase.CurrentVersionNo = 1;
        deathCase.ReviewVersionNo = 1;
        deathCase.NotifiedVersionNo = 1;
        deathCase.ObjectionStartsAt = Now.AddHours(-80);
        deathCase.ObjectionEndsAt = Now.AddHours(-8);
        DeathCaseVersion version = new DeathCaseVersion();
        version.CaseId = deathCase.Id;
        version.VersionNo = 1;
        version.SubmittedAt = Now.AddHours(-90);
        deathCase.Versions.Add(version);
        return deathCase;
    }

    private static DeathCaseVersion AddVersion(DeathCase deathCase, int versionNo)
    {
        DeathCaseVersion version = new DeathCaseVersion();
        version.CaseId = deathCase.Id;
        version.VersionNo = versionNo;
        deathCase.Versions.Add(version);
        return version;
    }

    private static DeathCaseFlag AddFlag(DeathCase deathCase, DeathCaseVersion version, DateTimeOffset? reviewedAt)
    {
        DeathCaseFlag flag = new DeathCaseFlag();
        flag.CaseId = deathCase.Id;
        flag.VersionId = version.Id;
        flag.Kind = DeathCaseFlagKind.CheckinAfterDeath;
        flag.ReviewedAt = reviewedAt;
        deathCase.Flags.Add(flag);
        return flag;
    }

    [Fact]
    public void Death09_ApproveBlockedBeforeObjectionEnds()
    {
        // Chuẩn bị: còn 1 giờ nữa mới hết 72 giờ phản đối
        DeathCase deathCase = NewReadyCase();
        deathCase.ObjectionEndsAt = Now.AddHours(1);

        // Hành động
        List<string> blockers = DeathCaseReviewRules.GetApproveBlockers(deathCase, Now);

        // Kiểm tra
        Assert.Equal(new List<string> { "OBJECTION_PERIOD_NOT_OVER" }, blockers);
    }

    [Fact]
    public void Death08_ApproveBlockedWhenOwnerNotNotified()
    {
        // Chuẩn bị: đồng hồ 72 giờ chưa chạy vì Owner chưa nhận được thư
        DeathCase deathCase = NewReadyCase();
        deathCase.NotifiedVersionNo = null;
        deathCase.ObjectionStartsAt = null;
        deathCase.ObjectionEndsAt = null;

        // Hành động
        List<string> blockers = DeathCaseReviewRules.GetApproveBlockers(deathCase, Now);

        // Kiểm tra
        Assert.Equal(new List<string> { "OWNER_NOT_NOTIFIED" }, blockers);
    }

    [Fact]
    public void Death10_UnreviewedFlagOfReviewVersionBlocksApprove()
    {
        // Chuẩn bị: một cờ của bản đang xét chưa được xem xét
        DeathCase deathCase = NewReadyCase();
        AddFlag(deathCase, deathCase.Versions[0], null);

        // Hành động
        List<string> blockers = DeathCaseReviewRules.GetApproveBlockers(deathCase, Now);

        // Kiểm tra
        Assert.Equal(new List<string> { "FLAGS_NOT_REVIEWED" }, blockers);
    }

    [Fact]
    public void Death10_FlagOfOlderVersionDoesNotBlockApprove()
    {
        // Chuẩn bị: bản 1 có cờ chưa xem xét, nhưng Verifier đang xét bản 2 (không cờ)
        DeathCase deathCase = NewReadyCase();
        DeathCaseVersion first = deathCase.Versions[0];
        AddVersion(deathCase, 2);
        deathCase.CurrentVersionNo = 2;
        deathCase.ReviewVersionNo = 2;
        AddFlag(deathCase, first, null);

        // Hành động
        List<string> blockers = DeathCaseReviewRules.GetApproveBlockers(deathCase, Now);

        // Kiểm tra
        Assert.Empty(blockers);
    }

    [Fact]
    public void Death04_NewerDraftBlocksApprove()
    {
        // Chuẩn bị: Executor đang có bản nháp số 2 trong khi Verifier xét bản 1
        DeathCase deathCase = NewReadyCase();
        AddVersion(deathCase, 2);
        deathCase.CurrentVersionNo = 2;

        // Hành động
        List<string> blockers = DeathCaseReviewRules.GetApproveBlockers(deathCase, Now);

        // Kiểm tra
        Assert.Equal(new List<string> { "NEWER_VERSION_PENDING" }, blockers);
    }

    [Fact]
    public void State_SupplementRequiredCannotBeApproved()
    {
        // Chuẩn bị
        DeathCase deathCase = NewReadyCase();
        deathCase.Status = DeathCaseStatus.SupplementRequired;

        // Hành động
        List<string> blockers = DeathCaseReviewRules.GetApproveBlockers(deathCase, Now);

        // Kiểm tra
        Assert.Equal(new List<string> { "NOT_UNDER_REVIEW" }, blockers);
    }

    [Fact]
    public void Death09_AllConditionsMetGivesNoBlockers()
    {
        // Chuẩn bị: có cờ nhưng đã xem xét, đúng lúc hết hạn phản đối
        DeathCase deathCase = NewReadyCase();
        deathCase.ObjectionEndsAt = Now;
        AddFlag(deathCase, deathCase.Versions[0], Now.AddHours(-1));

        // Hành động
        List<string> blockers = DeathCaseReviewRules.GetApproveBlockers(deathCase, Now);

        // Kiểm tra
        Assert.Empty(blockers);
    }

    [Fact]
    public void Death09_BlockersAccumulateInFixedOrder()
    {
        // Chuẩn bị: SupplementRequired + bản nháp mới + Owner chưa được báo + cờ chưa xem xét
        DeathCase deathCase = NewReadyCase();
        deathCase.Status = DeathCaseStatus.SupplementRequired;
        deathCase.ObjectionEndsAt = null;
        AddFlag(deathCase, deathCase.Versions[0], null);
        AddVersion(deathCase, 2);
        deathCase.CurrentVersionNo = 2;

        // Hành động
        List<string> blockers = DeathCaseReviewRules.GetApproveBlockers(deathCase, Now);

        // Kiểm tra
        Assert.Equal(new List<string>
        {
            "NOT_UNDER_REVIEW", "NEWER_VERSION_PENDING", "OWNER_NOT_NOTIFIED", "FLAGS_NOT_REVIEWED",
        }, blockers);
    }

    [Fact]
    public void Death01_RequestedPartsAreTrimmedDedupedAndSorted()
    {
        // Chuẩn bị
        List<string> parts = new List<string> { " FILES ", "DATE_OF_DEATH", "FILES", "ISSUED_BY" };

        // Hành động
        string stored = DeathCaseReviewRules.NormalizeRequestedParts(parts);
        List<string> split = DeathCaseReviewRules.SplitRequestedParts(stored);

        // Kiểm tra
        Assert.Equal("DATE_OF_DEATH,FILES,ISSUED_BY", stored);
        Assert.Equal(new List<string> { "DATE_OF_DEATH", "FILES", "ISSUED_BY" }, split);
        Assert.Empty(DeathCaseReviewRules.SplitRequestedParts(null));
    }

    [Fact]
    public void Death01_UnknownRequestedPartIsRejected()
    {
        // Chuẩn bị: mã sai chữ hoa/thường cũng không hợp lệ
        List<string> unknown = new List<string> { "FILES", "PASSPORT" };
        List<string> wrongCase = new List<string> { "files" };

        // Hành động
        AppException unknownError = Assert.Throws<AppException>(() => DeathCaseReviewRules.NormalizeRequestedParts(unknown));
        AppException wrongCaseError = Assert.Throws<AppException>(() => DeathCaseReviewRules.NormalizeRequestedParts(wrongCase));

        // Kiểm tra
        Assert.Equal(ErrorCode.ValidationError, unknownError.Code);
        Assert.Equal(ErrorCode.ValidationError, wrongCaseError.Code);
    }

    [Fact]
    public void Death01_EmptyRequestedPartsIsRejected()
    {
        // Chuẩn bị
        List<string> empty = new List<string>();

        // Hành động
        AppException nullError = Assert.Throws<AppException>(() => DeathCaseReviewRules.NormalizeRequestedParts(null));
        AppException emptyError = Assert.Throws<AppException>(() => DeathCaseReviewRules.NormalizeRequestedParts(empty));

        // Kiểm tra
        Assert.Equal(ErrorCode.ValidationError, nullError.Code);
        Assert.Equal(ErrorCode.ValidationError, emptyError.Code);
    }

    [Fact]
    public void Death01_MissingRequirementsMatchAllRequirementCodes()
    {
        // Chuẩn bị: bản nháp rỗng thiếu đủ mọi phần
        // Hành động
        List<string> allMissing = DeathCaseRules.GetMissingRequirements(null, null, null, null, null, null, 0);

        // Kiểm tra: danh sách mã cho phép khớp từng phần tử, đúng thứ tự
        Assert.Equal(new List<string>(DeathCaseRules.AllRequirementCodes), allMissing);
    }

    [Fact]
    public void Death03_BlankReasonIsRejected()
    {
        // Chuẩn bị
        string tooLong = new string('a', DeathCaseReviewRules.MaxReasonLength + 1);
        string exactlyMax = new string('b', DeathCaseReviewRules.MaxReasonLength);

        // Hành động
        AppException nullError = Assert.Throws<AppException>(() => DeathCaseReviewRules.NormalizeReason(null, "reason"));
        AppException blankError = Assert.Throws<AppException>(() => DeathCaseReviewRules.NormalizeReason("   ", "reason"));
        AppException longError = Assert.Throws<AppException>(() => DeathCaseReviewRules.NormalizeReason(tooLong, "note"));
        string trimmed = DeathCaseReviewRules.NormalizeReason("  Giấy mờ  ", "reason");
        string max = DeathCaseReviewRules.NormalizeReason(exactlyMax, "reason");

        // Kiểm tra
        Assert.Equal(ErrorCode.ValidationError, nullError.Code);
        Assert.Equal(ErrorCode.ValidationError, blankError.Code);
        Assert.Equal(ErrorCode.ValidationError, longError.Code);
        Assert.Equal("Giấy mờ", trimmed);
        Assert.Equal(DeathCaseReviewRules.MaxReasonLength, max.Length);
    }

    [Fact]
    public void State03_ClosedAndDecidableStatuses()
    {
        // Chuẩn bị / Hành động / Kiểm tra: duyệt, từ chối là trạng thái đóng; chỉ đang xét/chờ bổ sung mới quyết được
        foreach (DeathCaseStatus status in Enum.GetValues<DeathCaseStatus>())
        {
            bool expectedClosed = status == DeathCaseStatus.ApprovedForHandover || status == DeathCaseStatus.Rejected;
            bool expectedDecide = status == DeathCaseStatus.UnderReview || status == DeathCaseStatus.SupplementRequired;
            Assert.Equal(expectedClosed, DeathCaseReviewRules.IsClosed(status));
            Assert.Equal(expectedDecide, DeathCaseReviewRules.CanDecide(status));
        }
    }

    [Fact]
    public void Death04_FindReviewVersionReturnsReviewedVersionAndThrowsWhenUnsubmitted()
    {
        // Chuẩn bị
        DeathCase deathCase = NewReadyCase();
        AddVersion(deathCase, 2);
        deathCase.CurrentVersionNo = 2;
        DeathCase draft = NewReadyCase();
        draft.ReviewVersionNo = null;

        // Hành động
        DeathCaseVersion review = DeathCaseReviewRules.FindReviewVersion(deathCase);

        // Kiểm tra
        Assert.Equal(1, review.VersionNo);
        Assert.Throws<InvalidOperationException>(() => DeathCaseReviewRules.FindReviewVersion(draft));
    }
}
