using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Luật thuần của webhook SePay (SRS §8.5, PAY-01, PAY-05, PAY-06): kiểm key, tìm mã, giờ VN. Không cần database.</summary>
public sealed class SePayRulesTests
{
    // Key giả chỉ dùng trong test.
    private const string ConfiguredKey = "fake-key-for-tests";

    [Fact]
    public void Pay05_IsApiKeyValid_CorrectKey_ReturnsTrue()
    {
        // Chuẩn bị
        string header = "Apikey " + ConfiguredKey;

        // Hành động
        bool valid = SePayRules.IsApiKeyValid(ConfiguredKey, header);

        // Kiểm tra
        Assert.True(valid);
    }

    [Fact]
    public void Pay05_IsApiKeyValid_SchemeIsCaseInsensitive()
    {
        // Chuẩn bị
        string header = "apikey " + ConfiguredKey;

        // Hành động
        bool valid = SePayRules.IsApiKeyValid(ConfiguredKey, header);

        // Kiểm tra
        Assert.True(valid);
    }

    [Fact]
    public void Pay05_IsApiKeyValid_WrongKey_ReturnsFalse()
    {
        // Chuẩn bị
        string header = "Apikey fake-key-for-tests-wrong";

        // Hành động
        bool valid = SePayRules.IsApiKeyValid(ConfiguredKey, header);

        // Kiểm tra
        Assert.False(valid);
    }

    [Fact]
    public void Pay05_IsApiKeyValid_MissingOrEmptyHeader_ReturnsFalse()
    {
        // Chuẩn bị: không có gì

        // Hành động
        bool nullHeader = SePayRules.IsApiKeyValid(ConfiguredKey, null);
        bool emptyHeader = SePayRules.IsApiKeyValid(ConfiguredKey, "   ");
        bool schemeOnly = SePayRules.IsApiKeyValid(ConfiguredKey, "Apikey");
        bool emptyKey = SePayRules.IsApiKeyValid(ConfiguredKey, "Apikey ");

        // Kiểm tra
        Assert.False(nullHeader);
        Assert.False(emptyHeader);
        Assert.False(schemeOnly);
        Assert.False(emptyKey);
    }

    [Fact]
    public void Pay05_IsApiKeyValid_OtherScheme_ReturnsFalse()
    {
        // Chuẩn bị: gửi đúng key nhưng sai scheme (Bearer là của JWT người dùng)
        string header = "Bearer " + ConfiguredKey;

        // Hành động
        bool valid = SePayRules.IsApiKeyValid(ConfiguredKey, header);

        // Kiểm tra
        Assert.False(valid);
    }

    [Fact]
    public void Pay05_IsApiKeyValid_ServerKeyNotConfigured_RejectsEverything()
    {
        // Chuẩn bị: server chưa cấu hình key; kẻ gửi cũng gửi header có key rỗng hoặc key bất kỳ
        string header = "Apikey " + ConfiguredKey;

        // Hành động
        bool nullConfigured = SePayRules.IsApiKeyValid(null, header);
        bool emptyConfigured = SePayRules.IsApiKeyValid("", header);
        bool blankConfigured = SePayRules.IsApiKeyValid("  ", "Apikey  ");

        // Kiểm tra
        Assert.False(nullConfigured);
        Assert.False(emptyConfigured);
        Assert.False(blankConfigured);
    }

    [Fact]
    public void Pay02_FindPaymentCode_UsesSePayCodeWhenPresent()
    {
        // Chuẩn bị
        string code = "lvk7m2p9qx";
        string content = "Chuyen tien LVAAAAAA22";

        // Hành động
        string? found = SePayRules.FindPaymentCode(code, content);

        // Kiểm tra: ưu tiên code của SePay, trả chữ hoa
        Assert.Equal("LVK7M2P9QX", found);
    }

    [Fact]
    public void Pay02_FindPaymentCode_NoCode_FindsInContentIgnoringCase()
    {
        // Chuẩn bị
        string content = "nguyen van a chuyen tien lvk7m2p9qx cam on";

        // Hành động
        string? found = SePayRules.FindPaymentCode(null, content);

        // Kiểm tra
        Assert.Equal("LVK7M2P9QX", found);
    }

    [Fact]
    public void Pay02_FindPaymentCode_CodeGluedToBankSuffix_StillFindsCode()
    {
        // Chuẩn bị: ngân hàng hay nối mã giao dịch ngay sau nội dung
        string content = "LVK7M2P9QXFT26289123456";

        // Hành động
        string? found = SePayRules.FindPaymentCode("", content);

        // Kiểm tra
        Assert.Equal("LVK7M2P9QX", found);
    }

    [Fact]
    public void Pay02_FindPaymentCode_SkipsInvalidCandidateAndTakesFirstValid()
    {
        // Chuẩn bị: "LV0O1I..." chứa ký tự bị bỏ khỏi bảng mã (0, O, 1, I) nên không phải mã; mã hợp lệ đứng sau
        string content = "LV0O1I2345 LVBBBBBB22 LVCCCCCC33";

        // Hành động
        string? found = SePayRules.FindPaymentCode(null, content);

        // Kiểm tra
        Assert.Equal("LVBBBBBB22", found);
    }

    [Fact]
    public void Pay02_FindPaymentCode_NoCodeAnywhere_ReturnsNull()
    {
        // Chuẩn bị
        string content = "chuyen tien an trua";

        // Hành động
        string? fromContent = SePayRules.FindPaymentCode(null, content);
        string? fromNothing = SePayRules.FindPaymentCode(null, null);
        string? tooShort = SePayRules.FindPaymentCode(null, "LVAAAA");

        // Kiểm tra
        Assert.Null(fromContent);
        Assert.Null(fromNothing);
        Assert.Null(tooShort);
    }

    [Fact]
    public void Pay02_FindPaymentCode_CodeTooLongForColumn_FallsBackToContent()
    {
        // Chuẩn bị: code dài quá 16 ký tự không thể là mã đơn (10 ký tự), nên tìm trong nội dung
        string code = "THIS-CODE-IS-MUCH-TOO-LONG";
        string content = "LVBBBBBB22";

        // Hành động
        string? found = SePayRules.FindPaymentCode(code, content);

        // Kiểm tra
        Assert.Equal("LVBBBBBB22", found);
    }

    [Fact]
    public void Pay06_ParseVietnamTime_ConvertsVietnamLocalTimeToUtc()
    {
        // Chuẩn bị: 14:30:15 giờ VN = 07:30:15 UTC
        string text = "2026-10-19 14:30:15";

        // Hành động
        DateTimeOffset? parsed = SePayRules.ParseVietnamTime(text);

        // Kiểm tra
        Assert.NotNull(parsed);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 7, 30, 15, TimeSpan.Zero), parsed.Value);
        Assert.Equal(TimeSpan.Zero, parsed.Value.Offset);
    }

    [Fact]
    public void Pay06_ParseVietnamTime_JustAfterMidnightVietnamIsPreviousDayUtc()
    {
        // Chuẩn bị
        string text = "2026-10-19 00:00:01";

        // Hành động
        DateTimeOffset? parsed = SePayRules.ParseVietnamTime(text);

        // Kiểm tra
        Assert.NotNull(parsed);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 17, 0, 1, TimeSpan.Zero), parsed.Value);
    }

    [Fact]
    public void Pay06_ParseVietnamTime_InvalidText_ReturnsNull()
    {
        // Chuẩn bị: sai định dạng, rỗng, ngày không tồn tại

        // Hành động
        DateTimeOffset? isoFormat = SePayRules.ParseVietnamTime("2026-10-19T14:30:15");
        DateTimeOffset? garbage = SePayRules.ParseVietnamTime("không phải giờ");
        DateTimeOffset? empty = SePayRules.ParseVietnamTime("");
        DateTimeOffset? nothing = SePayRules.ParseVietnamTime(null);
        DateTimeOffset? impossibleDate = SePayRules.ParseVietnamTime("2026-02-30 10:00:00");

        // Kiểm tra
        Assert.Null(isoFormat);
        Assert.Null(garbage);
        Assert.Null(empty);
        Assert.Null(nothing);
        Assert.Null(impossibleDate);
    }

    [Fact]
    public void Pay05_Truncate_CutsLongTextAndTurnsBlankIntoNull()
    {
        // Chuẩn bị
        string longText = new string('a', 600);

        // Hành động
        string? cut = SePayRules.Truncate(longText, 512);
        string? same = SePayRules.Truncate("abc", 512);
        string? blank = SePayRules.Truncate("   ", 512);
        string? nothing = SePayRules.Truncate(null, 512);

        // Kiểm tra
        Assert.NotNull(cut);
        Assert.Equal(512, cut.Length);
        Assert.Equal("abc", same);
        Assert.Null(blank);
        Assert.Null(nothing);
    }
}
