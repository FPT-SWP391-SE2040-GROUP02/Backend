using System.Net;
using System.Text;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Ekyc;
using LegacyVault.Infrastructure.ServiceConfig;
using Xunit;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>
/// eKYC đọc địa chỉ, khóa và thời gian chờ từ cấu hình lúc chạy và tự chuyển sang địa chỉ dự phòng khi ô chính hỏng
/// (hết thời gian, lỗi mạng, 5xx, 401, 403, 429). Lỗi 4xx khác của dịch vụ giữ nguyên thông báo cũ và không chuyển. Không cần DB.
/// </summary>
public sealed class EkycFailoverTests
{
    private const string PrimaryHost = "primary.ekyc.test";
    private const string FallbackHost = "fallback.ekyc.test";
    private const string FrontJson = "{\"fields\":{\"id_number\":\"079200012345\",\"full_name\":\"NGUYEN VAN AN\"},\"warnings\":[],\"document_token\":\"abc\"}";

    private sealed class Setup
    {
        public RoutedHttpHandler Handler { get; } = new RoutedHttpHandler("X-Ekyc-Key");

        public FakeConfigResolver Resolver { get; } = new FakeConfigResolver();

        public ServiceStatusTracker Tracker { get; } = new ServiceStatusTracker();

        public EkycHttpClient Client()
        {
            return new EkycHttpClient(new HttpClient(Handler), Resolver, Tracker, TimeProvider.System);
        }
    }

    private static ResolvedConfig Config(string host, string? apiKey, string timeout = "60", string scheme = "http")
    {
        Dictionary<string, string> settings = new Dictionary<string, string>();
        settings["baseUrl"] = scheme + "://" + host + ":8879";
        settings["timeoutSeconds"] = timeout;
        Dictionary<string, string> secrets = new Dictionary<string, string>();
        if (apiKey != null)
        {
            secrets["apiKey"] = apiKey;
        }
        return FakeConfigResolver.Config(settings, secrets);
    }

    private static HttpResponseMessage Status(HttpStatusCode code)
    {
        return new HttpResponseMessage(code);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body)
    {
        HttpResponseMessage response = new HttpResponseMessage(code);
        response.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return response;
    }

    private static Setup WithBoth()
    {
        Setup setup = new Setup();
        setup.Resolver.Slots[SystemConfigSlot.Primary] = Config(PrimaryHost, "primary-key-1111");
        setup.Resolver.Slots[SystemConfigSlot.Fallback] = Config(FallbackHost, "fallback-key-2222");
        setup.Handler.ByHost[PrimaryHost] = () => Status(HttpStatusCode.OK);
        setup.Handler.ByHost[FallbackHost] = () => Status(HttpStatusCode.OK);
        return setup;
    }

    [Fact]
    public async Task UsesBaseUrlKeyAndPathFromConfig()
    {
        Setup setup = WithBoth();

        await setup.Client().DeleteDocumentAsync("tok 1", CancellationToken.None);

        RecordedCall call = setup.Handler.Calls.Single();
        Assert.Equal(PrimaryHost, call.Uri.Host);
        Assert.Equal(8879, call.Uri.Port);
        Assert.Equal("/api/documents/tok%201", call.Uri.AbsolutePath);
        Assert.Equal("primary-key-1111", call.Key);
        Assert.Equal(SystemConfigSlot.Primary, setup.Tracker.Get(SystemConfigServices.Ekyc).ActiveSlot);
    }

    [Fact]
    public async Task NoApiKey_SendsNoKeyHeader()
    {
        Setup setup = new Setup();
        setup.Resolver.Slots[SystemConfigSlot.Primary] = Config(PrimaryHost, null);
        setup.Handler.ByHost[PrimaryHost] = () => Status(HttpStatusCode.OK);

        await setup.Client().DeleteDocumentAsync("tok", CancellationToken.None);

        Assert.Null(setup.Handler.Calls.Single().Key);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task PrimaryFailsWithStatus_FallbackIsUsed(HttpStatusCode code)
    {
        Setup setup = WithBoth();
        setup.Handler.ByHost[PrimaryHost] = () => Status(code);

        await setup.Client().DeleteDocumentAsync("tok", CancellationToken.None);

        Assert.Equal(2, setup.Handler.Calls.Count);
        Assert.Equal(FallbackHost, setup.Handler.Calls[1].Uri.Host);
        Assert.Equal("fallback-key-2222", setup.Handler.Calls[1].Key);
        ServiceStatus status = setup.Tracker.Get(SystemConfigServices.Ekyc);
        Assert.Equal(SystemConfigSlot.Fallback, status.ActiveSlot);
        Assert.True(status.LastSucceeded);
    }

    [Fact]
    public async Task PrimaryNetworkErrorAndTimeout_FallbackIsUsed()
    {
        Setup network = WithBoth();
        network.Handler.ByHost[PrimaryHost] = () => throw new HttpRequestException("down");
        Setup timeout = WithBoth();
        timeout.Handler.ByHost[PrimaryHost] = () => throw new TaskCanceledException("timeout");

        await network.Client().DeleteDocumentAsync("tok", CancellationToken.None);
        await timeout.Client().DeleteDocumentAsync("tok", CancellationToken.None);

        Assert.Equal(2, network.Handler.Calls.Count);
        Assert.Equal(2, timeout.Handler.Calls.Count);
    }

    [Fact]
    public async Task ReadFront_PrimaryDown_FallbackReadsTheSameUpload()
    {
        Setup setup = WithBoth();
        setup.Handler.ByHost[PrimaryHost] = () => Status(HttpStatusCode.ServiceUnavailable);
        setup.Handler.ByHost[FallbackHost] = () => Json(HttpStatusCode.OK, FrontJson);

        EkycDocumentReading reading = await setup.Client().ReadFrontAsync(
            new byte[] { 1, 2, 3 }, "front.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal("079200012345", reading.IdNumber);
        Assert.Equal("abc", reading.DocumentToken);
        Assert.Equal(2, setup.Handler.Calls.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ImageRejected_DoesNotFailOver_AndKeepsTheServiceMessage(HttpStatusCode code)
    {
        Setup setup = WithBoth();
        setup.Handler.ByHost[PrimaryHost] = () => Json(code, "{\"detail\":\"Ảnh mờ\"}");

        AppException error = await Assert.ThrowsAsync<AppException>(() =>
            setup.Client().ReadFrontAsync(new byte[] { 1 }, "front.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycImageRejected, error.Code);
        Assert.Equal("Ảnh mờ", error.Message);
        Assert.Single(setup.Handler.Calls);
    }

    [Fact]
    public async Task BothFail_ThrowsServiceUnavailable_AfterExactlyTwoCalls()
    {
        Setup setup = WithBoth();
        setup.Handler.ByHost[PrimaryHost] = () => Status(HttpStatusCode.ServiceUnavailable);
        setup.Handler.ByHost[FallbackHost] = () => throw new HttpRequestException("down");

        AppException error = await Assert.ThrowsAsync<AppException>(() =>
            setup.Client().DeleteDocumentAsync("tok", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycServiceUnavailable, error.Code);
        Assert.Equal(AppErrorType.ServiceUnavailable, error.Type);
        Assert.Equal(2, setup.Handler.Calls.Count);
        Assert.Null(setup.Tracker.Get(SystemConfigServices.Ekyc).ActiveSlot);
    }

    [Fact]
    public async Task NoFallback_PrimaryDown_ThrowsServiceUnavailable()
    {
        Setup setup = new Setup();
        setup.Resolver.Slots[SystemConfigSlot.Primary] = Config(PrimaryHost, "k");
        setup.Handler.ByHost[PrimaryHost] = () => Status(HttpStatusCode.ServiceUnavailable);

        AppException error = await Assert.ThrowsAsync<AppException>(() =>
            setup.Client().DeleteDocumentAsync("tok", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycServiceUnavailable, error.Code);
        Assert.Single(setup.Handler.Calls);
        Assert.Equal("HTTP_503", setup.Tracker.Get(SystemConfigServices.Ekyc).LastErrorCode);
    }

    [Fact]
    public async Task NoBaseUrl_ThrowsServiceUnavailable_WithoutHttp()
    {
        Setup setup = new Setup();
        Dictionary<string, string> settings = new Dictionary<string, string>();
        settings["baseUrl"] = "";
        settings["timeoutSeconds"] = "60";
        setup.Resolver.Slots[SystemConfigSlot.Primary] = FakeConfigResolver.Config(settings, new Dictionary<string, string>());

        AppException error = await Assert.ThrowsAsync<AppException>(() =>
            setup.Client().DeleteDocumentAsync("tok", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycServiceUnavailable, error.Code);
        Assert.Empty(setup.Handler.Calls);
    }

    [Fact]
    public async Task CallerCancellation_IsRethrown()
    {
        Setup setup = WithBoth();
        using (CancellationTokenSource source = new CancellationTokenSource())
        {
            setup.Handler.ByHost[PrimaryHost] = () =>
            {
                source.Cancel();
                throw new TaskCanceledException("cancelled by caller");
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => setup.Client().DeleteDocumentAsync("tok", source.Token));
            Assert.Single(setup.Handler.Calls);
        }
    }
}
