using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Api.Exceptions;
using LegacyVault.Application.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>Real exception middleware and JSON writer, without SQL or external providers.</summary>
public sealed class GlobalExceptionHandlerTests
{
    [Theory]
    [InlineData(AppErrorType.Validation, ErrorCode.ValidationError, 400, "VALIDATION_ERROR")]
    [InlineData(AppErrorType.Unauthorized, ErrorCode.SessionExpired, 401, "SESSION_EXPIRED")]
    [InlineData(AppErrorType.Forbidden, ErrorCode.PaidTierRequired, 403, "PAID_TIER_REQUIRED")]
    [InlineData(AppErrorType.NotFound, ErrorCode.VaultNotFound, 404, "VAULT_NOT_FOUND")]
    [InlineData(AppErrorType.Conflict, ErrorCode.EditConflict, 409, "EDIT_CONFLICT")]
    [InlineData(AppErrorType.PayloadTooLarge, ErrorCode.FileTooLarge, 413, "FILE_TOO_LARGE")]
    [InlineData(AppErrorType.ServiceUnavailable, ErrorCode.EkycServiceUnavailable, 503, "EKYC_SERVICE_UNAVAILABLE")]
    public async Task BusinessErrorsPreserveStatusCodeAndExtensions(
        AppErrorType type, ErrorCode code, int status, string wireCode)
    {
        await using var server = await CreateServerAsync(new AppException(type, code, "Safe business message",
            new Dictionary<string, object?> { ["reason"] = "PLAN_LOCKED" }));
        using var client = server.GetTestClient();
        using var response = await client.GetAsync("/");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(wireCode, body.GetProperty("code").GetString());
        Assert.Equal("PLAN_LOCKED", body.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData(400, "VALIDATION_ERROR")]
    [InlineData(413, "FILE_TOO_LARGE")]
    public async Task InvalidRequestsAreNotInternalErrors(int status, string code)
    {
        await using var server = await CreateServerAsync(new BadHttpRequestException("private request detail", status));
        using var client = server.GetTestClient();
        using var response = await client.GetAsync("/");
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.DoesNotContain("private request detail", body.ToString());
    }

    [Fact]
    public async Task UnsupportedAcceptStillReturnsProblemJsonWithoutInternalDetails()
    {
        await using var server = await CreateServerAsync(new InvalidOperationException("private connection detail"));
        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await client.GetAsync("/");
        Assert.Equal(500, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INTERNAL_ERROR", body.GetProperty("code").GetString());
        Assert.DoesNotContain("private connection detail", body.ToString());
        Assert.False(body.TryGetProperty("exception", out _));
    }

    [Fact]
    public async Task AuthAndConcurrencyUseTheSameTopLevelCodeContract()
    {
        foreach (var (exception, status, code) in new (Exception, int, string)[]
        {
            (new InvalidCredentialsException(2), 401, "INVALID_CREDENTIALS"),
            (new DbUpdateConcurrencyException("private SQL detail"), 409, "EDIT_CONFLICT"),
        })
        {
            await using var server = await CreateServerAsync(exception);
            using var client = server.GetTestClient();
            using var response = await client.GetAsync("/");
            Assert.Equal(status, (int)response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(code, body.GetProperty("code").GetString());
            if (exception is InvalidCredentialsException)
                Assert.Equal(2, body.GetProperty("attemptsRemaining").GetInt32());
            Assert.DoesNotContain("private SQL detail", body.ToString());
        }
    }

    private static async Task<WebApplication> CreateServerAsync(Exception exception)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.Run(_ => throw exception);
        await app.StartAsync();
        return app;
    }
}
