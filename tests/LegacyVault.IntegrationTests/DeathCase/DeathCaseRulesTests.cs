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
}
