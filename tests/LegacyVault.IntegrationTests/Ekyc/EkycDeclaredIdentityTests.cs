using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>So thông tin Owner khai với OCR khi gắn danh tính lần đầu: ngày sinh chính xác, họ tên theo độ giống.</summary>
public sealed class EkycDeclaredIdentityTests
{
    private static readonly DateOnly Birth = new(1990, 5, 20);

    private static OwnerIdentityData Read(string name = "NGUYỄN VĂN AN")
    {
        return new OwnerIdentityData { CitizenId = "012345678901", FullName = name, DateOfBirth = Birth };
    }

    [Fact]
    public void SameNameWithoutDiacritics_Passes()
    {
        Assert.Null(EkycRules.FindDeclaredMismatch(Read(), "Nguyen  Van An", Birth, EkycRules.NameSimilarityThreshold));
    }

    [Fact]
    public void OneOcrCharacterOff_Passes()
    {
        // 1 ký tự lệch trên 13 ký tự: độ giống ~0,92 ≥ 0,85.
        Assert.Null(EkycRules.FindDeclaredMismatch(Read("NGUYEN VAN AM"), "Nguyễn Văn An", Birth, EkycRules.NameSimilarityThreshold));
    }

    [Fact]
    public void DifferentName_IsNameMismatch()
    {
        Assert.Equal("NAME_MISMATCH",
            EkycRules.FindDeclaredMismatch(Read(), "Trần Thị Bình", Birth, EkycRules.NameSimilarityThreshold));
    }

    [Fact]
    public void DifferentBirthDay_IsDobMismatchEvenIfNameMatches()
    {
        Assert.Equal("DOB_MISMATCH",
            EkycRules.FindDeclaredMismatch(Read(), "Nguyễn Văn An", Birth.AddDays(1), EkycRules.NameSimilarityThreshold));
    }

    [Fact]
    public void ThresholdIsConfigurable()
    {
        Assert.NotNull(EkycRules.FindDeclaredMismatch(Read("NGUYEN VAN AM"), "Nguyễn Văn An", Birth, 0.99));
    }

    [Fact]
    public void Similarity_Bounds()
    {
        Assert.Equal(1.0, EkycRules.NameSimilarity("An", "AN"));
        Assert.True(EkycRules.NameSimilarity("An", "Bình Minh") < 0.5);
    }
}
