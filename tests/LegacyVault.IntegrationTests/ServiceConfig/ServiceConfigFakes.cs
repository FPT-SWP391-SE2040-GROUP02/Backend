using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Enums;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>Bộ giải cấu hình giả trong bộ nhớ cho test không cần DB.</summary>
internal sealed class FakeConfigResolver : ISystemConfigResolver
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

    public static ResolvedConfig Config(Dictionary<string, string> settings, Dictionary<string, string> secrets)
    {
        ResolvedConfig config = new ResolvedConfig();
        config.Source = ConfigSources.Database;
        config.Settings = settings;
        config.Secrets = secrets;
        return config;
    }
}

/// <summary>Một lời gọi HTTP đã ghi lại: địa chỉ và giá trị của header khóa (nếu có).</summary>
internal sealed class RecordedCall
{
    public Uri Uri { get; set; } = new Uri("http://unset.test/");

    public string? Key { get; set; }
}

/// <summary>
/// Handler giả trả lời theo tên máy trong địa chỉ (primary.* hay fallback.*) để biết đang gọi ô nào, và ghi lại mọi lời gọi.
/// </summary>
internal sealed class RoutedHttpHandler : HttpMessageHandler
{
    private readonly string keyHeader;

    public RoutedHttpHandler(string keyHeader)
    {
        this.keyHeader = keyHeader;
    }

    public List<RecordedCall> Calls { get; } = new List<RecordedCall>();

    public Dictionary<string, Func<HttpResponseMessage>> ByHost { get; } = new Dictionary<string, Func<HttpResponseMessage>>();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RecordedCall call = new RecordedCall();
        call.Uri = request.RequestUri!;
        if (request.Headers.TryGetValues(keyHeader, out IEnumerable<string>? values))
        {
            call.Key = values.Single();
        }
        Calls.Add(call);
        return Task.FromResult(ByHost[request.RequestUri!.Host]());
    }
}

/// <summary>Dựng EkycHttpClient cho test: địa chỉ dịch vụ giả http://ekyc.test lấy từ ô chính của bộ giải cấu hình giả.</summary>
internal static class EkycTestClients
{
    public static LegacyVault.Infrastructure.Ekyc.EkycHttpClient Create(HttpMessageHandler handler)
    {
        FakeConfigResolver resolver = new FakeConfigResolver();
        Dictionary<string, string> settings = new Dictionary<string, string>();
        settings["baseUrl"] = "http://ekyc.test";
        settings["timeoutSeconds"] = "60";
        resolver.Slots[SystemConfigSlot.Primary] = FakeConfigResolver.Config(settings, new Dictionary<string, string>());
        return new LegacyVault.Infrastructure.Ekyc.EkycHttpClient(
            new HttpClient(handler),
            resolver,
            new LegacyVault.Infrastructure.ServiceConfig.ServiceStatusTracker(),
            TimeProvider.System);
    }
}
