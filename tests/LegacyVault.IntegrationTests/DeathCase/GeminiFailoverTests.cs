using System.Net;
using System.Text;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.DeathCases;
using LegacyVault.Infrastructure.ServiceConfig;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Gemini đọc model/khóa/thời gian chờ từ cấu hình lúc chạy và tự chuyển sang ô dự phòng khi ô chính hỏng
/// (hết thời gian, lỗi mạng, 5xx, 401, 403, 429). Kết quả nghiệp vụ và lỗi 400/404 không chuyển. Không cần DB.
/// </summary>
public sealed class GeminiFailoverTests
{
    private const string PrimaryKey = "primary-key-AAAA1111";
    private const string FallbackKey = "fallback-key-BBBB2222";

    private const string RecognizedJson =
        "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"isDeathCertificate\\\":true,\\\"confidence\\\":0.95,\\\"documentType\\\":\\\"Giấy chứng tử\\\"}\"}]}}]}";

    private const string NotCertificateJson =
        "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"isDeathCertificate\\\":false,\\\"confidence\\\":0.95,\\\"documentType\\\":\\\"Căn cước\\\"}\"}]}}]}";

    private sealed class Call
    {
        public string ApiKey { get; set; } = string.Empty;

        public Uri? Uri { get; set; }
    }

    /// <summary>Handler giả: trả lời theo khóa API trong header để biết đang gọi ô nào; ghi lại mọi lần gọi.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<Call> Calls { get; } = new List<Call>();

        public Dictionary<string, Func<HttpResponseMessage>> Responders { get; } = new Dictionary<string, Func<HttpResponseMessage>>();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Call call = new Call();
            call.ApiKey = request.Headers.GetValues("X-goog-api-key").Single();
            call.Uri = request.RequestUri;
            Calls.Add(call);
            return Task.FromResult(Responders[call.ApiKey]());
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler handler;

        public StubFactory(HttpMessageHandler handler)
        {
            this.handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, false);
        }
    }

    private sealed class FakeResolver : ISystemConfigResolver
    {
        public Dictionary<SystemConfigSlot, ResolvedConfig?> Slots { get; } = new Dictionary<SystemConfigSlot, ResolvedConfig?>();

        public Task<ResolvedConfig?> ResolveAsync(string serviceKey, SystemConfigSlot slot, CancellationToken ct)
        {
            ResolvedConfig? config = null;
            Slots.TryGetValue(slot, out config);
            return Task.FromResult(config);
        }

        public void Invalidate(string serviceKey)
        {
        }
    }

    private sealed class ListLogger : ILogger<GeminiDeathCertificateClassifier>
    {
        public List<string> Messages { get; } = new List<string>();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class Setup
    {
        public StubHandler Handler { get; } = new StubHandler();

        public FakeResolver Resolver { get; } = new FakeResolver();

        public ServiceStatusTracker Tracker { get; } = new ServiceStatusTracker();

        public ListLogger Logger { get; } = new ListLogger();

        public GeminiDeathCertificateClassifier Classifier()
        {
            return new GeminiDeathCertificateClassifier(
                new StubFactory(Handler), Resolver, Tracker, TimeProvider.System, Logger);
        }
    }

    private static ResolvedConfig Config(string? apiKey, string model = "gemini-test-model", string timeout = "20")
    {
        ResolvedConfig config = new ResolvedConfig();
        config.Source = ConfigSources.Database;
        config.Settings["model"] = model;
        config.Settings["timeoutSeconds"] = timeout;
        if (apiKey != null)
        {
            config.Secrets["apiKey"] = apiKey;
        }
        return config;
    }

    private static HttpResponseMessage Json(string body)
    {
        HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return response;
    }

    private static HttpResponseMessage Status(HttpStatusCode code)
    {
        return new HttpResponseMessage(code);
    }

    private static Task<DeathCertificateCheck> ClassifyAsync(Setup setup)
    {
        return setup.Classifier().ClassifyAsync(new byte[] { 1, 2, 3 }, "image/png", CancellationToken.None);
    }

    private static Setup WithBothSlots()
    {
        Setup setup = new Setup();
        setup.Resolver.Slots[SystemConfigSlot.Primary] = Config(PrimaryKey);
        setup.Resolver.Slots[SystemConfigSlot.Fallback] = Config(FallbackKey, "fallback-model");
        setup.Handler.Responders[FallbackKey] = () => Json(RecognizedJson);
        return setup;
    }

    [Fact]
    public async Task PrimarySucceeds_FallbackNotCalled()
    {
        Setup setup = WithBothSlots();
        setup.Handler.Responders[PrimaryKey] = () => Json(RecognizedJson);

        DeathCertificateCheck check = await ClassifyAsync(setup);

        Assert.Equal(DeathCertificateCheckStatus.Recognized, check.Status);
        Assert.Single(setup.Handler.Calls);
        Assert.Equal(SystemConfigSlot.Primary, setup.Tracker.Get(SystemConfigServices.Gemini).ActiveSlot);
    }

    [Fact]
    public async Task UsesModelKeyAndPathFromConfig()
    {
        Setup setup = new Setup();
        setup.Resolver.Slots[SystemConfigSlot.Primary] = Config(PrimaryKey, "gemini-2.5-flash");
        setup.Handler.Responders[PrimaryKey] = () => Json(RecognizedJson);

        await ClassifyAsync(setup);

        Call call = setup.Handler.Calls.Single();
        Assert.Equal(PrimaryKey, call.ApiKey);
        Assert.EndsWith("/v1beta/models/gemini-2.5-flash:generateContent", call.Uri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task PrimaryFailsWithStatus_FallbackIsUsed_AndTrackerReportsFallback(HttpStatusCode code)
    {
        Setup setup = WithBothSlots();
        setup.Handler.Responders[PrimaryKey] = () => Status(code);

        DeathCertificateCheck check = await ClassifyAsync(setup);

        Assert.Equal(DeathCertificateCheckStatus.Recognized, check.Status);
        Assert.Equal(2, setup.Handler.Calls.Count);
        Assert.EndsWith("fallback-model:generateContent", setup.Handler.Calls[1].Uri!.AbsolutePath);
        ServiceStatus status = setup.Tracker.Get(SystemConfigServices.Gemini);
        Assert.Equal(SystemConfigSlot.Fallback, status.ActiveSlot);
        Assert.True(status.LastSucceeded);
    }

    [Fact]
    public async Task PrimaryTimeout_FallbackIsUsed()
    {
        Setup setup = WithBothSlots();
        setup.Handler.Responders[PrimaryKey] = () => throw new TaskCanceledException("timeout");

        DeathCertificateCheck check = await ClassifyAsync(setup);

        Assert.Equal(DeathCertificateCheckStatus.Recognized, check.Status);
        Assert.Equal(2, setup.Handler.Calls.Count);
    }

    [Fact]
    public async Task PrimaryNetworkError_FallbackIsUsed()
    {
        Setup setup = WithBothSlots();
        setup.Handler.Responders[PrimaryKey] = () => throw new HttpRequestException("down");

        DeathCertificateCheck check = await ClassifyAsync(setup);

        Assert.Equal(DeathCertificateCheckStatus.Recognized, check.Status);
        Assert.Equal(2, setup.Handler.Calls.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Primary400And404_NoFailover_ReturnsUnavailable_AndTrackerRecordsFailure(HttpStatusCode code)
    {
        Setup setup = WithBothSlots();
        setup.Handler.Responders[PrimaryKey] = () => Status(code);

        DeathCertificateCheck check = await ClassifyAsync(setup);

        Assert.Equal(DeathCertificateCheckStatus.Unavailable, check.Status);
        Assert.Single(setup.Handler.Calls);
        ServiceStatus status = setup.Tracker.Get(SystemConfigServices.Gemini);
        Assert.False(status.LastSucceeded);
        Assert.Equal("HTTP_" + (int)code, status.LastErrorCode);
    }

    [Fact]
    public async Task NotRecognized_DoesNotFailOver()
    {
        Setup setup = WithBothSlots();
        setup.Handler.Responders[PrimaryKey] = () => Json(NotCertificateJson);

        DeathCertificateCheck check = await ClassifyAsync(setup);

        Assert.Equal(DeathCertificateCheckStatus.NotRecognized, check.Status);
        Assert.Single(setup.Handler.Calls);
    }

    [Fact]
    public async Task FallbackMissing_NoSecondCall()
    {
        Setup setup = new Setup();
        setup.Resolver.Slots[SystemConfigSlot.Primary] = Config(PrimaryKey);
        setup.Handler.Responders[PrimaryKey] = () => Status(HttpStatusCode.ServiceUnavailable);

        DeathCertificateCheck check = await ClassifyAsync(setup);

        Assert.Equal(DeathCertificateCheckStatus.Unavailable, check.Status);
        Assert.Single(setup.Handler.Calls);
        Assert.Equal("HTTP_503", setup.Tracker.Get(SystemConfigServices.Gemini).LastErrorCode);
    }

    [Fact]
    public async Task BothFail_ReturnsUnavailable_AfterExactlyTwoCalls()
    {
        Setup setup = WithBothSlots();
        setup.Handler.Responders[PrimaryKey] = () => Status(HttpStatusCode.ServiceUnavailable);
        setup.Handler.Responders[FallbackKey] = () => Status(HttpStatusCode.TooManyRequests);

        DeathCertificateCheck check = await ClassifyAsync(setup);

        Assert.Equal(DeathCertificateCheckStatus.Unavailable, check.Status);
        Assert.Equal(2, setup.Handler.Calls.Count);
        Assert.Null(setup.Tracker.Get(SystemConfigServices.Gemini).ActiveSlot);
    }

    [Fact]
    public async Task NoPrimaryKey_ReturnsUnavailableWithoutHttp()
    {
        Setup setup = new Setup();
        setup.Resolver.Slots[SystemConfigSlot.Primary] = Config(null);

        DeathCertificateCheck check = await ClassifyAsync(setup);

        Assert.Equal(DeathCertificateCheckStatus.Unavailable, check.Status);
        Assert.Empty(setup.Handler.Calls);
    }

    [Fact]
    public async Task NoPrimaryKey_ButFallbackHasKey_UsesFallback()
    {
        Setup setup = new Setup();
        setup.Resolver.Slots[SystemConfigSlot.Primary] = Config(null);
        setup.Resolver.Slots[SystemConfigSlot.Fallback] = Config(FallbackKey);
        setup.Handler.Responders[FallbackKey] = () => Json(RecognizedJson);

        DeathCertificateCheck check = await ClassifyAsync(setup);

        Assert.Equal(DeathCertificateCheckStatus.Recognized, check.Status);
        Assert.Equal(FallbackKey, setup.Handler.Calls.Single().ApiKey);
    }

    [Fact]
    public async Task CallerCancellation_IsRethrown_NotTreatedAsFailover()
    {
        Setup setup = WithBothSlots();
        using (CancellationTokenSource source = new CancellationTokenSource())
        {
            setup.Handler.Responders[PrimaryKey] = () =>
            {
                source.Cancel();
                throw new TaskCanceledException("cancelled by caller");
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => setup.Classifier().ClassifyAsync(new byte[] { 1 }, "image/png", source.Token));
            Assert.Single(setup.Handler.Calls);
        }
    }

    [Fact]
    public async Task Logs_NeverContainApiKey()
    {
        Setup setup = WithBothSlots();
        setup.Handler.Responders[PrimaryKey] = () => Status(HttpStatusCode.InternalServerError);
        setup.Handler.Responders[FallbackKey] = () => throw new HttpRequestException("boom " + FallbackKey);

        await ClassifyAsync(setup);

        Assert.NotEmpty(setup.Logger.Messages);
        Assert.All(setup.Logger.Messages, message =>
        {
            Assert.DoesNotContain(PrimaryKey, message);
            Assert.DoesNotContain(FallbackKey, message);
        });
    }
}
