using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Kiểm tra OpenAPI được sinh thật, không gọi Google/SQL hoặc tạo phiên.</summary>
public sealed class AuthOpenApiTests
{
    [Fact]
    public async Task GeneratedAuthContractDocumentsSchemasProofAndBearerWithoutChangingPublicAccess()
    {
        await using var factory = new OpenApiFactory();
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        Assert.DoesNotContain(paths.EnumerateObject(), path => path.Name.StartsWith("/v1/auth/"));
        // 12 route Auth gốc + 5 route xác thực hai bước của Admin (/api/v1/auth/2fa/*) + test-email (chỉ Admin).
        Assert.Equal(18, paths.EnumerateObject().Count(path => path.Name.StartsWith("/api/v1/auth/")));
        // Chỉ ẩn khỏi tài liệu: không xóa route runtime mà caller cũ có thể dùng.
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/v1/auth/csrf")).StatusCode);
        var endpoints = new[]
        {
            ("csrf", "get", "200", "AuthCsrfTokenDto"),
            ("register", "post", "202", ""),
            ("verify-email", "post", "200", "UserResponse"),
            ("resend-verification", "post", "202", ""),
            ("login", "post", "200", "AuthSessionResponse"),
            ("refresh", "post", "200", "AuthSessionResponse"),
            ("logout", "post", "204", ""),
            ("session", "get", "200", "UserResponse"),
            ("google", "post", "200", "GoogleLoginResponse"),
            ("google/link-confirm", "post", "200", "AuthSessionResponse"),
        };
        var protectedPosts = new[] { "login", "refresh", "logout", "google", "google/link-confirm" };
        foreach (var (route, method, status, schemaName) in endpoints)
        {
            var operation = paths.GetProperty("/api/v1/auth/" + route).GetProperty(method);
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
            Assert.Contains(operation.GetProperty("tags").EnumerateArray(), tag => tag.GetString() == "Auth");
            var response = operation.GetProperty("responses").GetProperty(status);
            if (schemaName.Length > 0)
            {
                var schema = response.GetProperty("content").GetProperty("application/json").GetProperty("schema");
                Assert.EndsWith("/" + schemaName, schema.GetProperty("$ref").GetString());
            }
            else Assert.False(response.TryGetProperty("content", out _));

            var parameters = operation.TryGetProperty("parameters", out var found) ? found.EnumerateArray().ToArray() : [];
            Assert.Equal(protectedPosts.Contains(route), parameters.Any(parameter =>
                parameter.GetProperty("name").GetString() == "X-CSRF-Token" &&
                parameter.GetProperty("in").GetString() == "header" &&
                parameter.GetProperty("required").GetBoolean()));
            var security = operation.GetProperty("security").EnumerateArray().ToArray();
            Assert.Contains(security, item => item.TryGetProperty("Bearer", out _));
            Assert.Equal(route != "session", security.Any(item => !item.EnumerateObject().Any()));
            if (protectedPosts.Contains(route)) Assert.True(operation.GetProperty("responses").TryGetProperty("429", out _));
        }
        Assert.Equal("bearer", document.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("Bearer").GetProperty("scheme").GetString());
        // Phương thức đăng nhập (AUTH-03) đã triển khai nên phải có trong tài liệu OpenAPI.
        Assert.True(paths.TryGetProperty("/api/v1/me/login-methods", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/login-methods/password/verify", out _));
        var examples = paths.GetProperty("/api/v1/auth/google").GetProperty("post").GetProperty("responses")
            .GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("examples");
        Assert.Equal(JsonValueKind.Object, examples.GetProperty("signedIn").GetProperty("value").GetProperty("session").ValueKind);
        Assert.Equal(JsonValueKind.Null, examples.GetProperty("linkRequired").GetProperty("value").GetProperty("session").ValueKind);
    }

    private sealed class OpenApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=127.0.0.1,1;Database=Unavailable;User Id=sa;Password=FixtureOnly;TrustServerCertificate=True;Connect Timeout=1",
                ["Email:Smtp:UserName"] = "", ["Email:Smtp:Password"] = "",
                ["CheckIn:Enabled"] = "false", ["DeathCase:NoticeJobEnabled"] = "false",
                ["Handover:Enabled"] = "false",
            }));
        }
    }
}
