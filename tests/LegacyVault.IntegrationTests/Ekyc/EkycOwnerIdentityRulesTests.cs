using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>Luật đọc và đối chiếu danh tính Owner (EKYC-24, EKYC-27) và cờ DEATH-13 đủ 12 số.</summary>
public sealed class EkycOwnerIdentityRulesTests
{
    private static EkycDocumentReading Reading(string id, string name, string birth)
    {
        return new EkycDocumentReading { IdNumber = id, FullName = name, DateOfBirth = birth };
    }

    private static OwnerIdentityData Expected()
    {
        OwnerIdentityData data = new OwnerIdentityData();
        data.CitizenId = "079200012345";
        data.FullName = "NGUYEN VAN AN";
        data.DateOfBirth = new DateOnly(2000, 2, 1);
        return data;
    }

    [Fact]
    public void TryRead_CompleteReading_NormalizesNameAndParsesBothDateFormats()
    {
        OwnerIdentityData? iso = EkycRules.TryReadOwnerIdentity(Reading("079200012345", " nguyen  van an ", "2000-02-01"));
        OwnerIdentityData? vn = EkycRules.TryReadOwnerIdentity(Reading("079200012345", "NGUYEN VAN AN", "01/02/2000"));

        Assert.NotNull(iso);
        Assert.Equal("NGUYEN VAN AN", iso!.FullName);
        Assert.Equal(new DateOnly(2000, 2, 1), iso.DateOfBirth);
        Assert.Equal(new DateOnly(2000, 2, 1), vn!.DateOfBirth);
    }

    [Theory]
    [InlineData("", "NGUYEN VAN AN", "01/02/2000")]
    [InlineData("07920001234", "NGUYEN VAN AN", "01/02/2000")]
    [InlineData("07920001234X", "NGUYEN VAN AN", "01/02/2000")]
    [InlineData("079200012345", "  ", "01/02/2000")]
    [InlineData("079200012345", "NGUYEN VAN AN", "")]
    [InlineData("079200012345", "NGUYEN VAN AN", "31/02/2000")]
    public void TryRead_IncompleteReading_ReturnsNull(string id, string name, string birth)
    {
        Assert.Null(EkycRules.TryReadOwnerIdentity(Reading(id, name, birth)));
    }

    [Fact]
    public void Compare_UsesSameRulesAsBeneficiary()
    {
        Assert.Equal(EkycIdentityMatch.Matched,
            EkycRules.CompareIdentity(Reading("079200012345", "nguyen van an", "2000-02-01"), Expected()));
        Assert.Equal(EkycIdentityMatch.WrongPerson,
            EkycRules.CompareIdentity(Reading("079200099999", "NGUYEN VAN AN", "2000-02-01"), Expected()));
        Assert.Equal(EkycIdentityMatch.NeedsReview,
            EkycRules.CompareIdentity(Reading("079200012345", "TRAN THI BICH", "2000-02-01"), Expected()));
        Assert.Equal(EkycIdentityMatch.NeedsReview,
            EkycRules.CompareIdentity(Reading("079200012345", "NGUYEN VAN AN", "2001-02-01"), Expected()));
    }

    [Fact]
    public void Death13_FullNumber_SameLast4ButDifferentId_IsMismatch()
    {
        // Hai số khác nhau nhưng trùng 4 số cuối: cách so 4 số cuối bỏ sót, so đủ 12 số thì bắt được.
        Assert.False(DeathCaseRules.IsIdentityMismatch("2345", "079211112345"));
        Assert.True(DeathCaseRules.IsIdentityMismatchFull("079200012345", "079211112345"));
    }

    [Fact]
    public void Death13_FullNumber_SameId_NoMismatch_AndMissingCaseIdNeverConcludes()
    {
        Assert.False(DeathCaseRules.IsIdentityMismatchFull("079200012345", "079200012345"));
        Assert.False(DeathCaseRules.IsIdentityMismatchFull("079200012345", null));
        Assert.False(DeathCaseRules.IsIdentityMismatchFull("079200012345", string.Empty));
    }
}
