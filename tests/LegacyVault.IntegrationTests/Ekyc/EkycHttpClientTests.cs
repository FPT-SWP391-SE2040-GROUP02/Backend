using System.Net;
using System.Text;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Infrastructure.Ekyc;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>EkycHttpClient đọc đúng JSON của dịch vụ eKYC (cấu trúc lấy từ ekyc-prototype server.py/logic.py).</summary>
public sealed class EkycHttpClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly string body;

        public StubHandler(HttpStatusCode status, string body)
        {
            this.status = status;
            this.body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new HttpResponseMessage(status);
            response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }
    }

    private static EkycHttpClient CreateClient(HttpStatusCode status, string body)
    {
        HttpClient http = new HttpClient(new StubHandler(status, body));
        http.BaseAddress = new Uri("http://ekyc.test/");
        return new EkycHttpClient(http);
    }

    private static readonly byte[] Image = new byte[] { 1, 2, 3 };

    [Fact]
    public async Task ReadFront_ReadsFieldsFromNestedObject()
    {
        string body = "{\"fields\":{\"id_number\":\"079200012345\",\"full_name\":\"NGUYEN VAN AN\","
            + "\"date_of_birth\":\"01/02/2000\",\"residence_address\":\"\"},\"warnings\":[\"OCR điểm thấp\"],"
            + "\"document_token\":\"abc\"}";
        EkycHttpClient client = CreateClient(HttpStatusCode.OK, body);

        EkycDocumentReading reading = await client.ReadFrontAsync(Image, "front.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal("079200012345", reading.IdNumber);
        Assert.Equal("NGUYEN VAN AN", reading.FullName);
        Assert.Equal("01/02/2000", reading.DateOfBirth);
        Assert.Equal("abc", reading.DocumentToken);
        Assert.Equal(new List<string> { "OCR điểm thấp" }, reading.Warnings);
    }

    [Fact]
    public async Task ReadFront_NullToken_MeansNoFace()
    {
        EkycHttpClient client = CreateClient(HttpStatusCode.OK, "{\"fields\":{},\"warnings\":[],\"document_token\":null}");

        EkycDocumentReading reading = await client.ReadFrontAsync(Image, "front.jpg", "image/jpeg", CancellationToken.None);

        Assert.Null(reading.DocumentToken);
    }

    [Fact]
    public async Task ReadFront_Http422_BecomesImageRejectedWithServiceMessage()
    {
        EkycHttpClient client = CreateClient(HttpStatusCode.UnprocessableEntity, "{\"detail\":\"Ảnh này là mặt sau.\"}");

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => client.ReadFrontAsync(Image, "front.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycImageRejected, error.Code);
        Assert.Equal("Ảnh này là mặt sau.", error.Message);
    }

    [Fact]
    public async Task CompareFace_Http410_BecomesDocumentExpired()
    {
        EkycHttpClient client = CreateClient(HttpStatusCode.Gone, "{\"detail\":\"hết hạn\"}");

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => client.CompareFaceAsync("abc", Image, "s.jpg", "image/jpeg", 0.363, CancellationToken.None));

        Assert.Equal(ErrorCode.EkycDocumentExpired, error.Code);
    }

    [Fact]
    public async Task CheckSpoof_Http503_BecomesServiceUnavailable()
    {
        EkycHttpClient client = CreateClient(HttpStatusCode.ServiceUnavailable, "{\"detail\":\"Model chưa chạy được.\"}");

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => client.CheckSpoofAsync(Image, "s.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycServiceUnavailable, error.Code);
        Assert.Equal(AppErrorType.ServiceUnavailable, error.Type);
    }

    [Fact]
    public async Task CompareFace_ReadsScoreAndMatched()
    {
        EkycHttpClient client = CreateClient(HttpStatusCode.OK, "{\"score\":0.586,\"matched\":true,\"threshold\":0.363}");

        EkycFaceComparison result = await client.CompareFaceAsync(
            "abc", Image, "s.jpg", "image/jpeg", 0.363, CancellationToken.None);

        Assert.Equal(0.586, result.Score);
        Assert.True(result.Matched);
    }

    [Fact]
    public async Task NoBaseAddress_IsServiceUnavailable()
    {
        EkycHttpClient client = new EkycHttpClient(new HttpClient(new StubHandler(HttpStatusCode.OK, "{}")));

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => client.CheckSpoofAsync(Image, "s.jpg", "image/jpeg", CancellationToken.None));

        Assert.Equal(ErrorCode.EkycServiceUnavailable, error.Code);
    }
}
