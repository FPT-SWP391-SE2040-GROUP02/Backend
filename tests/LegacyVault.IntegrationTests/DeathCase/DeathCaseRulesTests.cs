using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Luật thuần của hồ sơ chứng tử (không cần DB).</summary>
public sealed class DeathCaseRulesTests
{
    [Fact]
    public void Death05_OnlyRunningPlanCanOpenCase()
    {
        // Chuẩn bị
        PlanStatus[] running = { PlanStatus.Active, PlanStatus.CheckinOverdue, PlanStatus.AwaitingCheck };

        foreach (PlanStatus status in Enum.GetValues<PlanStatus>())
        {
            // Hành động
            bool canOpen = DeathCaseRules.CanOpen(status);

            // Kiểm tra
            bool expected = Array.IndexOf(running, status) >= 0;
            Assert.Equal(expected, canOpen);
        }
    }

    [Fact]
    public void Death05_DraftPlanCannotOpenCase()
    {
        // Chuẩn bị
        PlanStatus status = PlanStatus.Draft;

        // Hành động
        bool canOpen = DeathCaseRules.CanOpen(status);

        // Kiểm tra
        Assert.False(canOpen);
    }

    [Fact]
    public void Death01_OnlyTheCertificateFileIsRequiredToSubmit()
    {
        // Họ tên, số giấy, ngày mất, ngày cấp, nơi cấp không còn bắt buộc: chỉ cần có giấy chứng tử.
        List<string> noCertificate = DeathCaseRules.GetMissingRequirements(0);
        List<string> hasCertificate = DeathCaseRules.GetMissingRequirements(1);

        Assert.Equal(new List<string> { "FILES" }, noCertificate);
        Assert.Empty(hasCertificate);
    }

    [Fact]
    public void Death01_OnlyDeathCertificateFilesCountTowardsTheRequirement()
    {
        DeathCaseFile certificate = new DeathCaseFile();
        certificate.Kind = DeathCaseFileKind.DeathCertificate;
        DeathCaseFile notice = new DeathCaseFile();
        notice.Kind = DeathCaseFileKind.DeathNotice;

        // Giấy báo tử đứng một mình không đủ để nộp.
        Assert.Equal(0, DeathCaseRules.CountCertificateFiles(new List<DeathCaseFile> { notice }));
        Assert.Equal(1, DeathCaseRules.CountCertificateFiles(new List<DeathCaseFile> { notice, certificate }));
        Assert.Equal(new List<string> { "FILES" },
            DeathCaseRules.GetMissingRequirements(DeathCaseRules.CountCertificateFiles(new List<DeathCaseFile> { notice })));
    }

    [Fact]
    public void Death01_FileKindOnlyAcceptsKnownValues()
    {
        Assert.True(DeathCaseFileKind.IsValid("DEATH_CERTIFICATE"));
        Assert.True(DeathCaseFileKind.IsValid("DEATH_NOTICE"));
        Assert.False(DeathCaseFileKind.IsValid("OTHER"));
        Assert.False(DeathCaseFileKind.IsValid(null));
    }

    [Fact]
    public void Death01_DeathDateInFutureIsRejected()
    {
        // Chuẩn bị
        DateOnly today = new DateOnly(2026, 10, 15);

        // Hành động
        AppException futureDeath = Assert.Throws<AppException>(
            () => DeathCaseRules.ValidateDates(today.AddDays(1), null, today));
        AppException issuedBeforeDeath = Assert.Throws<AppException>(
            () => DeathCaseRules.ValidateDates(today.AddDays(-2), today.AddDays(-3), today));
        AppException futureIssued = Assert.Throws<AppException>(
            () => DeathCaseRules.ValidateDates(today.AddDays(-2), today.AddDays(1), today));

        // Kiểm tra
        Assert.Equal(ErrorCode.ValidationError, futureDeath.Code);
        Assert.Equal(ErrorCode.ValidationError, issuedBeforeDeath.Code);
        Assert.Equal(ErrorCode.ValidationError, futureIssued.Code);
        // Ngày mất hôm nay, cấp cùng ngày: hợp lệ; trường chưa nhập thì bỏ qua.
        DeathCaseRules.ValidateDates(today, today, today);
        DeathCaseRules.ValidateDates(null, null, today);
    }

    [Fact]
    public void Death11_ChangingOwnerIdentityOrDeathDateIsCritical()
    {
        // Chuẩn bị
        DateOnly death = new DateOnly(2026, 10, 1);

        // Hành động
        bool nameChanged = DeathCaseRules.IsCriticalChange(
            "Nguyễn Văn An", "012345678901", death, "Nguyễn Văn Bình", "012345678901", death);
        bool citizenIdChanged = DeathCaseRules.IsCriticalChange(
            "Nguyễn Văn An", "012345678901", death, "Nguyễn Văn An", "012345678902", death);
        bool deathDateChanged = DeathCaseRules.IsCriticalChange(
            "Nguyễn Văn An", "012345678901", death, "Nguyễn Văn An", "012345678901", death.AddDays(-1));
        bool onlySpacingAndCase = DeathCaseRules.IsCriticalChange(
            "Nguyễn Văn An", "012345678901", death, "  nguyễn   văn AN ", "012345678901", death);

        // Kiểm tra
        Assert.True(nameChanged);
        Assert.True(citizenIdChanged);
        Assert.True(deathDateChanged);
        Assert.False(onlySpacingAndCase);
    }

    [Fact]
    public void Death11_ChangingCertificatePlaceIsNotCritical()
    {
        // Chuẩn bị: số giấy, nơi cấp, ngày cấp không có trong tham số so sánh trọng yếu
        DateOnly death = new DateOnly(2026, 10, 1);

        // Hành động
        bool critical = DeathCaseRules.IsCriticalChange(
            "Nguyễn Văn An", "012345678901", death, "Nguyễn Văn An", "012345678901", death);

        // Kiểm tra
        Assert.False(critical);
    }

    [Fact]
    public void Death08_ObjectionEndsSeventyTwoHoursAfterDelivery()
    {
        // Chuẩn bị
        DateTimeOffset deliveredAt = new DateTimeOffset(2026, 10, 15, 8, 30, 0, TimeSpan.Zero);

        // Hành động
        DateTimeOffset endsAt = DeathCaseRules.ObjectionEndsAt(deliveredAt);

        // Kiểm tra
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 8, 30, 0, TimeSpan.Zero), endsAt);
    }
}
