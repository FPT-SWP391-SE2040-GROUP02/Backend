using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Settings;

/// <summary>Luật kiểm giá trị tham số Admin (không cần DB).</summary>
public sealed class SystemSettingRulesTests
{
    [Theory]
    [InlineData("Executor.InvitationExpiresDays", "7")]
    [InlineData("Executor.InvitationExpiresDays", "1")]
    [InlineData("Executor.InvitationExpiresDays", "60")]
    [InlineData("Executor.ReminderAfterHours", "1440")]
    [InlineData("Handover.ResponseWindowDays", "1")]
    [InlineData("Handover.GrantWindowDays", " 30 ")]
    [InlineData("Features.MaintenanceMode", "true")]
    [InlineData("Time.DemoSeconds", "false")]
    [InlineData("Company.Email", "ho-tro@legacyvault.vn")]
    [InlineData("Company.Email", "")]
    [InlineData("Company.Address", "")]
    [InlineData("Plans.Xs.Price1Y", "199000")]
    [InlineData("Khoa.Khong.Co.Luat", "bat-ky")]
    public void FindValueError_ValidValueOrUnknownKey_ReturnsNull(string key, string value)
    {
        // Hành động
        string? error = SystemSettingRules.FindValueError(key, value);

        // Kiểm tra
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Executor.InvitationExpiresDays", "0")]
    [InlineData("Executor.InvitationExpiresDays", "61")]
    [InlineData("Executor.InvitationExpiresDays", "-1")]
    [InlineData("Executor.InvitationExpiresDays", "abc")]
    [InlineData("Executor.InvitationExpiresDays", "2.5")]
    [InlineData("Executor.InvitationExpiresDays", "")]
    [InlineData("Executor.ReminderAfterHours", "0")]
    [InlineData("Executor.ReminderAfterHours", "1441")]
    [InlineData("Handover.GrantWindowDays", "366")]
    [InlineData("Handover.ResponseWindowDays", "0")]
    [InlineData("Security.MaxPinAttempts", "0")]
    [InlineData("Security.PinLockoutMinutes", "99999")]
    [InlineData("Limits.MaxUploadsPerHour", "0")]
    [InlineData("Features.MaintenanceMode", "yes")]
    [InlineData("Time.DemoSeconds", "1")]
    [InlineData("Company.Email", "khong-co-a-cong")]
    [InlineData("Company.Email", "co khoang@trang.vn")]
    public void FindValueError_BadValue_ReturnsVietnameseMessage(string key, string value)
    {
        // Hành động
        string? error = SystemSettingRules.FindValueError(key, value);

        // Kiểm tra
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void FindValueError_TooLongCompanyName_IsRejected()
    {
        // Hành động
        string? error = SystemSettingRules.FindValueError("Company.Name", new string('a', 101));

        // Kiểm tra
        Assert.NotNull(error);
        Assert.Contains("100", error);
    }

    [Fact]
    public void FindRelationError_DefaultsAreConsistent()
    {
        // Hành động và kiểm tra: không có giá trị nào thì dùng mặc định, và mặc định phải hợp lệ.
        Assert.Null(SystemSettingRules.FindRelationError(new Dictionary<string, long>()));
    }

    [Theory]
    [InlineData(7, 48, true)]
    [InlineData(1, 23, true)]
    [InlineData(1, 24, false)]
    [InlineData(2, 100, false)]
    public void FindRelationError_ExecutorReminderMustComeBeforeInvitationExpires(long days, long hours, bool valid)
    {
        // Chuẩn bị
        Dictionary<string, long> effective = new Dictionary<string, long>();
        effective[SystemSettingRules.ExecutorExpiresKey] = days;
        effective[SystemSettingRules.ExecutorReminderKey] = hours;

        // Hành động
        string? error = SystemSettingRules.FindRelationError(effective);

        // Kiểm tra
        Assert.Equal(valid, error == null);
    }
}

/// <summary>
/// Admin lưu tham số qua API: giá trị sai bị từ chối 400 và không ghi vào DB. Đổi cấu hình dùng chung của cả tiến trình
/// nên chạy riêng, không song song với test khác. Chỉ chạy khi có LEGACYVAULT_TEST_CONNECTION.
/// </summary>
[Collection(SettingsCollection.Name)]
public sealed class SystemSettingValidationFlowTests : IAsyncLifetime
{
    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        RuntimeSettings.Replace(new Dictionary<string, string>());
        if (factory == null)
        {
            return;
        }
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        // Lần đọc đầu tạo các tham số mặc định.
        HttpResponseMessage seeded = await client.GetAsync("/api/v1/admin/settings");
        Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string key, string value, bool? isEnabled = null)
    {
        return client.PutAsJsonAsync($"/api/v1/admin/settings/{key}", new { value, isEnabled });
    }

    private static Task<HttpResponseMessage> BatchAsync(HttpClient client, params (string Key, string Value)[] items)
    {
        List<object> list = new List<object>();
        foreach ((string key, string value) in items)
        {
            list.Add(new { key, value });
        }
        return client.PutAsJsonAsync("/api/v1/admin/settings/batch", new { items = list });
    }

    private static async Task<string> ReadValueAsync(HttpClient client, string key)
    {
        HttpResponseMessage response = await client.GetAsync($"/api/v1/admin/settings/{key}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        return body.GetProperty("value").GetString()!;
    }

    [SqlFact]
    public async Task Put_ValueOutsideTheRange_Returns400AndKeepsTheOldValue()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage response = await PutAsync(admin, "Executor.InvitationExpiresDays", "99");

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        // Màn Cấu hình hiện câu này (trường title), nên câu phải nói rõ khoảng hợp lệ.
        Assert.Contains("từ 1 đến 60", problem.GetProperty("title").GetString());
        Assert.Equal("7", await ReadValueAsync(admin, "Executor.InvitationExpiresDays"));
    }

    [SqlFact]
    public async Task Put_NotANumber_Returns400()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage response = await PutAsync(admin, "Handover.GrantWindowDays", "abc");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("30", await ReadValueAsync(admin, "Handover.GrantWindowDays"));
    }

    [SqlFact]
    public async Task Put_ValidValue_IsSaved()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage response = await PutAsync(admin, "Executor.InvitationExpiresDays", "10");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("10", await ReadValueAsync(admin, "Executor.InvitationExpiresDays"));
    }

    [SqlFact]
    public async Task Put_ReminderNotBeforeInvitationExpiry_Returns400()
    {
        // Chuẩn bị: nhắc sau 48 giờ (mặc định); hạn lời mời 3 ngày (72 giờ) vẫn hợp lệ.
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage shorter = await PutAsync(admin, "Executor.InvitationExpiresDays", "3");
        HttpResponseMessage late = await PutAsync(admin, "Executor.ReminderAfterHours", "100");

        // Kiểm tra: nhắc sau 100 giờ thì lời mời đã hết hạn (72 giờ) nên bị từ chối.
        Assert.Equal(HttpStatusCode.OK, shorter.StatusCode);
        await ApiTestHelpers.AssertProblemAsync(late, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("48", await ReadValueAsync(admin, "Executor.ReminderAfterHours"));
    }

    [SqlFact]
    public async Task Batch_ReminderAfterExpiry_Returns400AndSavesNothing()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage response = await BatchAsync(
            admin,
            ("Executor.InvitationExpiresDays", "1"),
            ("Executor.ReminderAfterHours", "30"),
            ("Handover.GrantWindowDays", "40"));

        // Kiểm tra: cả lô bị từ chối nên ngay cả giá trị đúng (GrantWindowDays) cũng chưa được lưu.
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("7", await ReadValueAsync(admin, "Executor.InvitationExpiresDays"));
        Assert.Equal("48", await ReadValueAsync(admin, "Executor.ReminderAfterHours"));
        Assert.Equal("30", await ReadValueAsync(admin, "Handover.GrantWindowDays"));
    }

    [SqlFact]
    public async Task Batch_ValidPair_IsSavedTogether()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();

        // Hành động: đổi cả hai trong một lần, thứ tự trong lô không quan trọng.
        HttpResponseMessage response = await BatchAsync(
            admin,
            ("Executor.ReminderAfterHours", "60"),
            ("Executor.InvitationExpiresDays", "3"));

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("3", await ReadValueAsync(admin, "Executor.InvitationExpiresDays"));
        Assert.Equal("60", await ReadValueAsync(admin, "Executor.ReminderAfterHours"));
    }

    [SqlFact]
    public async Task Put_BadCompanyEmail_Returns400()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage response = await PutAsync(admin, "Company.Email", "khong-phai-email");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task Put_DisablingASettingDoesNotRequireAGoodValue()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();

        // Hành động: tắt tham số thì hệ thống dùng mức gốc, giá trị gõ vào không có tác dụng.
        HttpResponseMessage response = await PutAsync(admin, "Executor.InvitationExpiresDays", "7", isEnabled: false);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
