using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Settings;

/// <summary>Luật kiểm giá trị tham số Admin (không cần DB).</summary>
public sealed class SystemSettingRulesTests
{
    [Theory]
    [InlineData("CheckIn.MaxLateReminders", "3")]
    [InlineData("CheckIn.MaxLateReminders", "1")]
    [InlineData("CheckIn.MaxLateReminders", "5")]
    [InlineData("CheckIn.FirstReminderDaysBeforeDue", "60")]
    [InlineData("CheckIn.SecondReminderDaysBeforeDue", "1")]
    [InlineData("Handover.GrantWindowDays", " 30 ")]
    [InlineData("Features.MaintenanceMode", "true")]
    [InlineData("Time.DemoSeconds", "false")]
    [InlineData("Time.AuthFollowsDemoClock", "true")]
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
    [InlineData("CheckIn.MaxLateReminders", "0")]
    [InlineData("CheckIn.MaxLateReminders", "6")]
    [InlineData("CheckIn.MaxLateReminders", "-1")]
    [InlineData("CheckIn.MaxLateReminders", "abc")]
    [InlineData("CheckIn.MaxLateReminders", "2.5")]
    [InlineData("CheckIn.MaxLateReminders", "")]
    [InlineData("CheckIn.FirstReminderDaysBeforeDue", "61")]
    [InlineData("CheckIn.FirstReminderDaysBeforeDue", "1")]
    [InlineData("CheckIn.SecondReminderDaysBeforeDue", "0")]
    [InlineData("CheckIn.SecondReminderDaysBeforeDue", "31")]
    [InlineData("CheckIn.ExecutorProxyLimit", "21")]
    [InlineData("Handover.ResponseWindowDays", "0")]
    [InlineData("Security.MaxPinAttempts", "0")]
    [InlineData("Security.PinLockoutMinutes", "99999")]
    [InlineData("Limits.MaxUploadsPerHour", "0")]
    [InlineData("Features.MaintenanceMode", "yes")]
    [InlineData("Time.DemoSeconds", "1")]
    [InlineData("Time.AuthFollowsDemoClock", "co")]
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
    [InlineData(7, 1, true)]
    [InlineData(3, 2, true)]
    [InlineData(2, 1, true)]
    [InlineData(1, 1, false)]
    [InlineData(3, 3, false)]
    [InlineData(2, 5, false)]
    public void FindRelationError_FirstReminderMustBeFartherThanSecond(long first, long second, bool valid)
    {
        // Chuẩn bị
        Dictionary<string, long> effective = new Dictionary<string, long>();
        effective[SystemSettingRules.FirstReminderKey] = first;
        effective[SystemSettingRules.SecondReminderKey] = second;

        // Hành động
        string? error = SystemSettingRules.FindRelationError(effective);

        // Kiểm tra
        Assert.Equal(valid, error == null);
    }

    [Theory]
    [InlineData(48, 72, true)]
    [InlineData(48, 49, true)]
    [InlineData(48, 48, false)]
    [InlineData(72, 48, false)]
    public void FindRelationError_IncidentEscalateMustComeAfterReminder(long reminder, long escalate, bool valid)
    {
        // Chuẩn bị
        Dictionary<string, long> effective = new Dictionary<string, long>();
        effective[SystemSettingRules.IncidentReminderKey] = reminder;
        effective[SystemSettingRules.IncidentEscalateKey] = escalate;

        // Hành động
        string? error = SystemSettingRules.FindRelationError(effective);

        // Kiểm tra
        Assert.Equal(valid, error == null);
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
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
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
        HttpResponseMessage response = await PutAsync(admin, "CheckIn.MaxLateReminders", "9");

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        // Màn Cấu hình hiện câu này (trường title), nên câu phải nói rõ khoảng hợp lệ.
        Assert.Contains("từ 1 đến 5", problem.GetProperty("title").GetString());
        Assert.Equal("3", await ReadValueAsync(admin, "CheckIn.MaxLateReminders"));
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
        HttpResponseMessage response = await PutAsync(admin, "CheckIn.MaxLateReminders", "4");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("4", await ReadValueAsync(admin, "CheckIn.MaxLateReminders"));
    }

    [SqlFact]
    public async Task Put_FirstReminderNotFartherThanSecond_Returns400()
    {
        // Chuẩn bị: thư thứ hai đang là 1 (mặc định); đặt thư thứ nhất bằng 1 là trùng nhau.
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage equal = await PutAsync(admin, "CheckIn.FirstReminderDaysBeforeDue", "2");
        HttpResponseMessage valid = await PutAsync(admin, "CheckIn.SecondReminderDaysBeforeDue", "2");

        // Kiểm tra: 2 so với 1 hợp lệ, rồi thư thứ hai lên 2 thì trùng thư thứ nhất (2) nên bị từ chối.
        Assert.Equal(HttpStatusCode.OK, equal.StatusCode);
        await ApiTestHelpers.AssertProblemAsync(valid, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("1", await ReadValueAsync(admin, "CheckIn.SecondReminderDaysBeforeDue"));
    }

    [SqlFact]
    public async Task Batch_SecondFartherThanFirst_Returns400AndSavesNothing()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage response = await BatchAsync(
            admin,
            ("CheckIn.FirstReminderDaysBeforeDue", "3"),
            ("CheckIn.SecondReminderDaysBeforeDue", "5"),
            ("CheckIn.MaxLateReminders", "4"));

        // Kiểm tra: cả lô bị từ chối nên ngay cả giá trị đúng (MaxLateReminders) cũng chưa được lưu.
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("7", await ReadValueAsync(admin, "CheckIn.FirstReminderDaysBeforeDue"));
        Assert.Equal("1", await ReadValueAsync(admin, "CheckIn.SecondReminderDaysBeforeDue"));
        Assert.Equal("3", await ReadValueAsync(admin, "CheckIn.MaxLateReminders"));
    }

    [SqlFact]
    public async Task Batch_ValidPair_IsSavedTogether()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();

        // Hành động: đổi cả hai trong một lần, thứ tự trong lô không quan trọng.
        HttpResponseMessage response = await BatchAsync(
            admin,
            ("CheckIn.SecondReminderDaysBeforeDue", "2"),
            ("CheckIn.FirstReminderDaysBeforeDue", "3"));

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("3", await ReadValueAsync(admin, "CheckIn.FirstReminderDaysBeforeDue"));
        Assert.Equal("2", await ReadValueAsync(admin, "CheckIn.SecondReminderDaysBeforeDue"));
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
        HttpResponseMessage response = await PutAsync(admin, "CheckIn.MaxLateReminders", "3", isEnabled: false);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
