using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

public sealed class SimulatedEkycClientTests
{
    private static readonly byte[] Image = new byte[] { 1, 2, 3 };

    [Fact]
    public async Task ReadFront_ForOwner_GivesStableTwelveDigitIdPerAccountAndDifferentAcrossAccounts()
    {
        // Chuẩn bị
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        // Hành động
        EkycDocumentReading a1 = await new SimulatedEkycClient(first).ReadFrontAsync(Image, "f.jpg", "image/jpeg", default);
        EkycDocumentReading a2 = await new SimulatedEkycClient(first).ReadFrontAsync(Image, "f.jpg", "image/jpeg", default);
        EkycDocumentReading b = await new SimulatedEkycClient(second).ReadFrontAsync(Image, "f.jpg", "image/jpeg", default);

        // Kiểm tra: đủ điều kiện gắn danh tính chủ tài sản; cùng tài khoản cùng số, khác tài khoản khác số
        Assert.NotNull(EkycRules.TryReadOwnerIdentity(a1));
        Assert.Equal(12, a1.IdNumber.Length);
        Assert.Equal(a1.IdNumber, a2.IdNumber);
        Assert.NotEqual(a1.IdNumber, b.IdNumber);
    }

    [Fact]
    public async Task LivenessSpoofAndFaceMatch_AlwaysPass()
    {
        // Chuẩn bị
        SimulatedEkycClient client = new SimulatedEkycClient(Guid.NewGuid());

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

    [Fact]
    public void Options_PausedOnStartupIsHonoredUntilAdminToggles()
    {
        // Chuẩn bị: cấu hình khởi động ở trạng thái tạm ngừng
        EkycOptions options = new EkycOptions { PausedOnStartup = true };
        Assert.True(options.Paused);

        // Hành động: Admin bật lại eKYC thật bằng công cụ thử nghiệm
        options.SetPaused(false);

        // Kiểm tra: lựa chọn của Admin thắng cấu hình khởi động
        Assert.False(options.Paused);
    }
}
