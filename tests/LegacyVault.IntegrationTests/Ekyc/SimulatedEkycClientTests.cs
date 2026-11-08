using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

public sealed class SimulatedEkycClientTests
{
    private static readonly byte[] Image = new byte[] { 1, 2, 3 };

    [Fact]
    public async Task ReadFront_ForBeneficiary_ReturnsExactlyTheExpectedPerson()
    {
        // Chuẩn bị: người nhận được chủ kho khai trước
        BeneficiaryPersonalData expected = new BeneficiaryPersonalData
        {
            CitizenId = "079123456789",
            FullName = "Trần Minh Anh",
            DateOfBirth = new DateOnly(1995, 3, 14),
        };
        SimulatedEkycClient client = new SimulatedEkycClient(expected, Guid.NewGuid());

        // Hành động
        EkycDocumentReading reading = await client.ReadFrontAsync(Image, "front.jpg", "image/jpeg", CancellationToken.None);

        // Kiểm tra: đối chiếu danh tính ra Matched, có token để sang bước sau
        Assert.Equal(EkycIdentityMatch.Matched, EkycRules.CompareIdentity(reading, expected));
        Assert.StartsWith(SimulatedEkycClient.TokenPrefix, reading.DocumentToken);
    }

    [Fact]
    public async Task ReadFront_ForOwner_GivesStableTwelveDigitIdPerAccountAndDifferentAcrossAccounts()
    {
        // Chuẩn bị
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        // Hành động
        EkycDocumentReading a1 = await new SimulatedEkycClient(null, first).ReadFrontAsync(Image, "f.jpg", "image/jpeg", default);
        EkycDocumentReading a2 = await new SimulatedEkycClient(null, first).ReadFrontAsync(Image, "f.jpg", "image/jpeg", default);
        EkycDocumentReading b = await new SimulatedEkycClient(null, second).ReadFrontAsync(Image, "f.jpg", "image/jpeg", default);

        // Kiểm tra: đủ điều kiện gắn danh tính chủ kho; cùng tài khoản cùng số, khác tài khoản khác số
        Assert.NotNull(EkycRules.TryReadOwnerIdentity(a1));
        Assert.Equal(12, a1.IdNumber.Length);
        Assert.Equal(a1.IdNumber, a2.IdNumber);
        Assert.NotEqual(a1.IdNumber, b.IdNumber);
    }

    [Fact]
    public async Task LivenessSpoofAndFaceMatch_AlwaysPass()
    {
        // Chuẩn bị
        SimulatedEkycClient client = new SimulatedEkycClient(null, Guid.NewGuid());

        // Hành động
        EkycLivenessState started = await client.StartLivenessAsync(default);
        EkycLivenessState frame = await client.SendLivenessFrameAsync(started.SessionId, Image, "f.jpg", "image/jpeg", default);
        EkycSpoofCheck spoof = await client.CheckSpoofAsync(Image, "s.jpg", "image/jpeg", default);
        EkycFaceComparison match = await client.CompareFaceAsync("paused-x", Image, "s.jpg", "image/jpeg", 0.6, default);

        // Kiểm tra
        Assert.True(frame.Passed);
        Assert.True(spoof.Passed);
        Assert.True(match.Score >= 0.6);
    }

    [Fact]
    public async Task FaceEmbeddings_AreIdenticalSoOwnerFaceCheckAlwaysMatches()
    {
        // Chuẩn bị
        SimulatedEkycClient client = new SimulatedEkycClient(null, Guid.NewGuid());

        // Hành động
        EkycFaceEmbedding one = await client.GetFaceEmbeddingAsync(Image, "a.jpg", "image/jpeg", default);
        EkycFaceEmbedding two = await client.GetFaceEmbeddingAsync(new byte[] { 9 }, "b.jpg", "image/jpeg", default);

        // Kiểm tra
        Assert.True(EkycRules.CosineSimilarity(one.Values, two.Values) > 0.999);
    }

    [Fact]
    public void Options_PausedDefaultsToFalseAndCanBeToggled()
    {
        // Chuẩn bị
        EkycOptions options = new EkycOptions();
        Assert.False(options.Paused);

        // Hành động
        options.SetPaused(true);

        // Kiểm tra
        Assert.True(options.Paused);
    }
}
