using System.Net;
using System.Text;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Infrastructure.Ekyc;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>EkycHttpClient gọi và đọc đúng các điểm cuối /api/liveness của dịch vụ eKYC (server.py, logic.py).</summary>
public sealed class EkycLivenessHttpClientTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly byte[] body;
        private readonly string contentType;

        public RecordingHandler(HttpStatusCode status, string json)
            : this(status, Encoding.UTF8.GetBytes(json), "application/json")
        {
        }

        public RecordingHandler(HttpStatusCode status, byte[] body, string contentType)
        {
            this.status = status;
            this.body = body;
            this.contentType = contentType;
        }

        public string? Method { get; private set; }

        public string? Path { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method.Method;
            Path = request.RequestUri!.AbsolutePath;
            RequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            HttpResponseMessage response = new HttpResponseMessage(status);
            response.Content = new ByteArrayContent(body);
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return response;
        }
    }

    private static EkycHttpClient CreateClient(RecordingHandler handler)
    {
        HttpClient http = new HttpClient(handler);
        http.BaseAddress = new Uri("http://ekyc.test/");
        return new EkycHttpClient(http);
    }

    private const string StartJson = "{\"session_id\":\"sess-1\",\"expires_in\":90,"
        + "\"actions\":[{\"key\":\"blink\",\"label\":\"Nhắm mắt\"},{\"key\":\"left\",\"label\":\"Quay trái\"}],"
        + "\"completed\":[],\"current_index\":0,\"prompt\":\"Nhìn thẳng\",\"calibrated\":false,"
        + "\"challenge_completed\":false,\"pad_score\":null,\"pad_samples\":0,\"pad_threshold\":0.7,"
        + "\"pad_passed\":false,\"prototype_passed\":false,\"failed_reason\":\"\",\"frames\":0}";

    [Fact]
    public async Task Start_ReadsSessionActionsAndDropsScores()
    {
        RecordingHandler handler = new RecordingHandler(HttpStatusCode.OK, StartJson);
        EkycHttpClient client = CreateClient(handler);

        EkycLivenessState state = await client.StartLivenessAsync(CancellationToken.None);

        Assert.Equal("POST", handler.Method);
        Assert.Equal("/api/liveness/start", handler.Path);
        Assert.Equal("sess-1", state.SessionId);
        Assert.Equal(90, state.ExpiresInSeconds);
        Assert.Equal(new List<string> { "blink", "left" }, state.Actions.Select(a => a.Key).ToList());
        Assert.Equal("Quay trái", state.Actions[1].Label);
        Assert.Equal("Nhìn thẳng", state.Prompt);
        Assert.False(state.Passed);
    }

    [Fact]
    public async Task Frame_SendsSessionIdAndReadsProgress()
    {
        string json = "{\"actions\":[],\"completed\":[\"blink\"],\"current_index\":1,\"prompt\":\"Quay trái\","
            + "\"calibrated\":true,\"challenge_completed\":true,\"pad_passed\":true,\"prototype_passed\":true,"
            + "\"failed_reason\":\"\",\"frame_hint\":\"ok\"}";
        RecordingHandler handler = new RecordingHandler(HttpStatusCode.OK, json);
        EkycHttpClient client = CreateClient(handler);

        EkycLivenessState state = await client.SendLivenessFrameAsync(
            "sess-1", new byte[] { 1, 2, 3 }, "f.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal("/api/liveness/frame", handler.Path);
        Assert.Contains("name=session_id", handler.RequestBody);
        Assert.Contains("sess-1", handler.RequestBody);
        Assert.Contains("name=frame", handler.RequestBody);
        Assert.Equal(new List<string> { "blink" }, state.Completed);
        Assert.Equal(1, state.CurrentIndex);
        Assert.True(state.Calibrated);
        Assert.True(state.ChallengeCompleted);
        Assert.True(state.PadPassed);
        Assert.True(state.Passed);
        Assert.Equal("ok", state.FrameHint);
    }

    [Fact]
    public async Task Frame_Http410_BecomesLivenessRequired()
    {
        EkycHttpClient client = CreateClient(new RecordingHandler(HttpStatusCode.Gone, "{\"detail\":\"Phiên hết hạn\"}"));

        AppException error = await Assert.ThrowsAsync<AppException>(() => client.SendLivenessFrameAsync(
            "sess-1", new byte[] { 1 }, "f.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycLivenessRequired, error.Code);
    }

    [Fact]
    public async Task Frame_Http422_BecomesImageRejected()
    {
        EkycHttpClient client = CreateClient(
            new RecordingHandler(HttpStatusCode.UnprocessableEntity, "{\"detail\":\"Ảnh không đọc được.\"}"));

        AppException error = await Assert.ThrowsAsync<AppException>(() => client.SendLivenessFrameAsync(
            "sess-1", new byte[] { 1 }, "f.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycImageRejected, error.Code);
    }

    [Fact]
    public async Task Start_Http429_BecomesServiceUnavailable()
    {
        EkycHttpClient client = CreateClient(
            new RecordingHandler(HttpStatusCode.TooManyRequests, "{\"detail\":\"Đang có nhiều phiên\"}"));

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => client.StartLivenessAsync(CancellationToken.None));

        Assert.Equal(ErrorCode.EkycServiceUnavailable, error.Code);
    }

    [Fact]
    public async Task Selfie_ReturnsImageBytes()
    {
        byte[] jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        RecordingHandler handler = new RecordingHandler(HttpStatusCode.OK, jpeg, "image/jpeg");
        EkycHttpClient client = CreateClient(handler);

        byte[] selfie = await client.GetLivenessSelfieAsync("sess-1", CancellationToken.None);

        Assert.Equal("GET", handler.Method);
        Assert.Equal("/api/liveness/sess-1/selfie", handler.Path);
        Assert.Equal(jpeg, selfie);
    }

    [Fact]
    public async Task Selfie_Http409_BecomesLivenessNotPassed()
    {
        EkycHttpClient client = CreateClient(
            new RecordingHandler(HttpStatusCode.Conflict, "{\"detail\":\"Phiên chưa đạt\"}"));

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => client.GetLivenessSelfieAsync("sess-1", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycLivenessNotPassed, error.Code);
        Assert.Equal(AppErrorType.Conflict, error.Type);
    }

    [Fact]
    public async Task Selfie_Http410_BecomesLivenessRequired()
    {
        EkycHttpClient client = CreateClient(new RecordingHandler(HttpStatusCode.Gone, "{\"detail\":\"hết hạn\"}"));

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => client.GetLivenessSelfieAsync("sess-1", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycLivenessRequired, error.Code);
    }

    [Fact]
    public async Task Delete_CallsDeleteOnTheSession()
    {
        RecordingHandler handler = new RecordingHandler(HttpStatusCode.OK, "{\"deleted\":true}");
        EkycHttpClient client = CreateClient(handler);

        await client.DeleteLivenessAsync("sess-1", CancellationToken.None);

        Assert.Equal("DELETE", handler.Method);
        Assert.Equal("/api/liveness/sess-1", handler.Path);
    }

    [Fact]
    public async Task Delete_Failure_IsServiceUnavailable()
    {
        EkycHttpClient client = CreateClient(new RecordingHandler(HttpStatusCode.InternalServerError, "{}"));

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => client.DeleteLivenessAsync("sess-1", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycServiceUnavailable, error.Code);
    }
}
