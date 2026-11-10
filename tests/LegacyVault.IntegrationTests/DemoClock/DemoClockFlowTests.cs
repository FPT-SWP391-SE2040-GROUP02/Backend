using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Repositories;
using LegacyVault.Infrastructure.Services;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LegacyVault.IntegrationTests.DemoClocks;

/// <summary>
/// Nút "Tua thời gian" qua HTTP trên SQL Server thật: đồng hồ DEMO, việc nền chạy ở từng mốc, email nhắc được gửi
/// bằng bộ gửi giả, token vẫn dùng được sau khi tua. Chỉ chạy khi có LEGACYVAULT_TEST_CONNECTION.
/// </summary>
public sealed class DemoClockFlowTests : IAsyncLifetime
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private ApiFactory factory = null!;
    private readonly FakeCheckInReminderSender reminders = new FakeCheckInReminderSender();

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        factory.ConfigureAdditionalServices = services =>
        {
            services.RemoveAll<ICheckInReminderSender>();
            services.AddSingleton<ICheckInReminderSender>(reminders);
        };
        await factory.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
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
        return client;
    }

    /// <summary>Ngày (giờ Việt Nam) của một thời điểm, dạng yyyy-MM-dd, để gửi cho API tua.</summary>
    private static string VietnamDate(DateTimeOffset moment)
    {
        DateTimeOffset local = moment.ToOffset(VietnamOffset);
        return local.ToString("yyyy-MM-dd");
    }

    private static Task<HttpResponseMessage> TravelAsync(HttpClient client, string targetDate)
    {
        return client.PostAsJsonAsync("/api/v1/dev/clock/travel", new { targetDate });
    }

    [SqlFact]
    public async Task GetClock_WithoutLogin_ReturnsRealTimeAndNotSimulated()
    {
        // Chuẩn bị
        HttpClient anonymous = factory.CreateClient();

        // Hành động
        HttpResponseMessage response = await anonymous.GetAsync("/api/v1/dev/clock");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.False(body.GetProperty("isSimulated").GetBoolean());
        Assert.Equal(0, body.GetProperty("offsetSeconds").GetInt64());
        Assert.Equal(
            body.GetProperty("realNowUtc").GetDateTimeOffset().ToUnixTimeSeconds(),
            body.GetProperty("nowUtc").GetDateTimeOffset().ToUnixTimeSeconds());
    }

    [SqlFact]
    public async Task Travel_WithoutLogin_Returns401()
    {
        // Chuẩn bị
        HttpClient anonymous = factory.CreateClient();

        // Hành động
        HttpResponseMessage response = await TravelAsync(anonymous, "2030-01-01");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SqlFact]
    public async Task Travel_AsOwner_Returns404LikeOtherDevTools()
    {
        // Chuẩn bị
        User owner = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, owner);

        // Hành động
        HttpResponseMessage response = await TravelAsync(client, "2030-01-01");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "USER_NOT_FOUND");
    }

    [SqlFact]
    public async Task Travel_ToTodayOrThePast_Returns400AndClockUnchanged()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();
        string yesterday = VietnamDate(DateTimeOffset.UtcNow.AddDays(-1));

        // Hành động
        HttpResponseMessage response = await TravelAsync(admin, yesterday);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("targetDate", out JsonElement _));
        HttpResponseMessage clock = await admin.GetAsync("/api/v1/dev/clock");
        JsonElement status = await ApiTestHelpers.ReadJsonAsync(clock);
        Assert.False(status.GetProperty("isSimulated").GetBoolean());
    }

    [SqlFact]
    public async Task Travel_MoreThanFourHundredDays_Returns400()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();
        string tooFar = VietnamDate(DateTimeOffset.UtcNow.AddDays(DemoClockRules.MaxTravelDays + 2));

        // Hành động
        HttpResponseMessage response = await TravelAsync(admin, tooFar);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("targetDate", out JsonElement _));
    }

    [SqlFact]
    public async Task Travel_WithBadDateText_Returns400()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage response = await TravelAsync(admin, "31/12/2030");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task Travel_PastEveryCheckInMilestone_SendsEveryReminderAndMovesPlanToAwaitingCheck()
    {
        // Chuẩn bị: Chủ kho có kế hoạch đang chạy và đã điểm danh (mở kỳ đầu, hạn sau 30 ngày).
        DeathCaseTestData.OwnerVault owner = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        HttpResponseMessage checkIn = await owner.Client.PostAsync("/api/v1/check-ins", null);
        Assert.Equal(HttpStatusCode.OK, checkIn.StatusCode);
        CheckInPeriod period = await LoadOpenPeriodAsync(owner.PlanId);
        HttpClient admin = await AdminClientAsync();
        string targetDate = VietnamDate(period.GraceEndsAt.AddDays(2));

        // Hành động
        HttpResponseMessage response = await TravelAsync(admin, targetDate);

        // Kiểm tra: mỗi mốc nhắc gửi đúng một thư theo thứ tự thời gian, các bước cũng theo thứ tự đó.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        List<CheckInReminderKind> expectedKinds = new List<CheckInReminderKind>(
            CheckInTimeline.ActiveKinds(period.MaxLateReminders));
        Assert.Equal(expectedKinds, reminders.Sent);

        List<string> stepKinds = new List<string>();
        DateTimeOffset previous = DateTimeOffset.MinValue;
        foreach (JsonElement step in body.GetProperty("steps").EnumerateArray())
        {
            stepKinds.Add(step.GetProperty("kind").GetString()!);
            DateTimeOffset at = step.GetProperty("at").GetDateTimeOffset();
            Assert.True(at >= previous);
            previous = at;
        }
        Assert.Contains(DemoClockRules.KindCheckInReminder, stepKinds);
        Assert.Contains(DemoClockRules.KindCheckInDue, stepKinds);
        Assert.Contains(DemoClockRules.KindCheckInGraceEnd, stepKinds);
        Assert.Equal(DemoClockRules.KindTargetDay, stepKinds[stepKinds.Count - 1]);
        int emails = 0;
        foreach (JsonElement step in body.GetProperty("steps").EnumerateArray())
        {
            emails += step.GetProperty("emailsSent").GetInt32();
        }
        Assert.Equal(expectedKinds.Count, emails);

        PlanStatus status = PlanStatus.Draft;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            status = await db.HandoverPlans.Where(p => p.Id == owner.PlanId).Select(p => p.Status).SingleAsync();
        });
        Assert.Equal(PlanStatus.AwaitingCheck, status);
        Assert.True(body.GetProperty("clock").GetProperty("isSimulated").GetBoolean());
        DateTimeOffset clockNow = body.GetProperty("clock").GetProperty("nowUtc").GetDateTimeOffset();
        Assert.Equal(VietnamDate(clockNow), targetDate);
        Assert.Equal(9, clockNow.ToOffset(VietnamOffset).Hour);
    }

    [SqlFact]
    public async Task Travel_StopsInTheMiddleOfTheCheckInCycle_OnlySendsRemindersAlreadyReached()
    {
        // Chuẩn bị
        DeathCaseTestData.OwnerVault owner = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await owner.Client.PostAsync("/api/v1/check-ins", null);
        CheckInPeriod period = await LoadOpenPeriodAsync(owner.PlanId);
        HttpClient admin = await AdminClientAsync();
        // Dừng ngày hạn trừ 5 ngày: mới qua mốc "còn 7 ngày".
        string targetDate = VietnamDate(period.DueAt.AddDays(-5));

        // Hành động
        HttpResponseMessage response = await TravelAsync(admin, targetDate);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new List<CheckInReminderKind> { CheckInReminderKind.DueInSevenDays }, reminders.Sent);
        PlanStatus status = PlanStatus.Draft;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            status = await db.HandoverPlans.Where(p => p.Id == owner.PlanId).Select(p => p.Status).SingleAsync();
        });
        Assert.Equal(PlanStatus.Active, status);
    }

    [SqlFact]
    public async Task Reset_ReturnsToRealTimeAndForgetsTheStoredOffset()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();
        string target = VietnamDate(DateTimeOffset.UtcNow.AddDays(3));
        HttpResponseMessage travel = await TravelAsync(admin, target);
        Assert.Equal(HttpStatusCode.OK, travel.StatusCode);
        long? savedAfterTravel = await ReadSavedOffsetAsync();
        Assert.NotNull(savedAfterTravel);
        Assert.True(savedAfterTravel.Value > 0);

        // Token Admin cũ đã hết hạn theo giờ mới; đăng nhập lại (token mới) rồi mới về ngày thật.
        User secondAdmin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient freshAdmin = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(freshAdmin, factory.Services, secondAdmin);

        // Hành động
        HttpResponseMessage reset = await freshAdmin.PostAsync("/api/v1/dev/clock/reset", null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        JsonElement status = await ApiTestHelpers.ReadJsonAsync(reset);
        Assert.False(status.GetProperty("isSimulated").GetBoolean());
        Assert.Equal(0, status.GetProperty("offsetSeconds").GetInt64());
        long? savedAfterReset = await ReadSavedOffsetAsync();
        Assert.Equal(0L, savedAfterReset);
    }

    [SqlFact]
    public async Task Loader_RestoresStoredOffsetWhenServerStarts()
    {
        // Chuẩn bị: độ lệch 2 giờ đã nằm trong DB, đồng hồ đang ở giờ thật.
        IDemoClock clock = factory.Services.GetRequiredService<IDemoClock>();
        clock.SetOffset(TimeSpan.Zero);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DemoClockRepository repository = new DemoClockRepository(db);
            await repository.SaveOffsetSecondsAsync(7200, DateTimeOffset.UtcNow, CancellationToken.None);
        });
        DemoClockLoader loader = new DemoClockLoader(
            clock, factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<DemoClockLoader>.Instance);

        // Hành động
        await loader.StartAsync(CancellationToken.None);

        // Kiểm tra
        Assert.Equal(TimeSpan.FromHours(2), clock.Offset);
        clock.SetOffset(TimeSpan.Zero);
    }

    [SqlFact]
    public async Task AfterTravelingThirtyDays_OldTokenIsExpiredButNewTokenIsAccepted()
    {
        // Chuẩn bị: Chủ kho đăng nhập (token sống 15 phút) rồi Admin tua 30 ngày.
        User owner = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient oldSession = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(oldSession, factory.Services, owner);
        await ApiTestHelpers.CreateVaultAsync(oldSession);
        HttpClient admin = await AdminClientAsync();
        string target = VietnamDate(DateTimeOffset.UtcNow.AddDays(30));

        // Hành động
        HttpResponseMessage travel = await TravelAsync(admin, target);
        HttpResponseMessage oldToken = await oldSession.GetAsync("/api/v1/check-ins/me");
        HttpClient newSession = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(newSession, factory.Services, owner);
        HttpResponseMessage newToken = await newSession.GetAsync("/api/v1/check-ins/me");

        // Kiểm tra: token cũ hết hạn theo giờ mới, token tạo sau khi tua vẫn hợp lệ (không bị 401).
        Assert.Equal(HttpStatusCode.OK, travel.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldToken.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, newToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newToken.StatusCode);
    }

    [SqlFact]
    public async Task TwoTravelsAtTheSameTime_SecondOneGetsBusyOrBothSucceedInOrder()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();
        string first = VietnamDate(DateTimeOffset.UtcNow.AddDays(2));
        string second = VietnamDate(DateTimeOffset.UtcNow.AddDays(4));

        // Hành động
        Task<HttpResponseMessage> a = TravelAsync(admin, first);
        Task<HttpResponseMessage> b = TravelAsync(admin, second);
        HttpResponseMessage[] responses = await Task.WhenAll(a, b);

        // Kiểm tra: không có lỗi 500; lần bị chặn (nếu có) báo DEMO_CLOCK_BUSY, lần còn lại thành công.
        foreach (HttpResponseMessage response in responses)
        {
            Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.True(
                response.StatusCode == HttpStatusCode.OK
                || response.StatusCode == HttpStatusCode.Conflict
                || response.StatusCode == HttpStatusCode.BadRequest);
        }
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
    }

    private async Task<CheckInPeriod> LoadOpenPeriodAsync(Guid planId)
    {
        CheckInPeriod? period = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            period = await db.CheckInPeriods.AsNoTracking()
                .Where(p => p.PlanId == planId && p.ClosedAt == null)
                .SingleAsync();
        });
        Assert.NotNull(period);
        return period;
    }

    private async Task<long?> ReadSavedOffsetAsync()
    {
        long? saved = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DemoClockRepository repository = new DemoClockRepository(db);
            saved = await repository.FindOffsetSecondsAsync(CancellationToken.None);
        });
        return saved;
    }
}
