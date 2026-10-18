using LegacyVault.Api.Security;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LegacyVault.Api.Config;

/// <summary>Metadata Swagger cho Auth; không thay đổi authentication/antiforgery runtime.</summary>
public static class AuthOpenApiConfig
{
    public static void Configure(OpenApiOptions options)
    {
        options.AddOperationTransformer((operation, context, ct) =>
        {
            if (context.Description.ActionDescriptor is not ControllerActionDescriptor
                { ControllerName: "Auth" or "GoogleAuth" }) return Task.CompletedTask;
            // MVC có thể kế thừa application/json từ controller cho cả response không body.
            foreach (var status in new[] { "202", "204", "429" })
            {
                if (operation.Responses?.TryGetValue(status, out var response) == true && response is OpenApiResponse bodyless)
                    bodyless.Content = null;
            }
            if (context.Description.ActionDescriptor is ControllerActionDescriptor
                { ControllerName: "GoogleAuth", ActionName: "Login" } &&
                operation.Responses?["200"].Content?.TryGetValue("application/json", out var media) == true)
            {
                media.Examples = new Dictionary<string, IOpenApiExample>
                {
                    ["signedIn"] = new OpenApiExample
                    {
                        Summary = "Đã đăng nhập — có session và refresh cookie",
                        Value = JsonNode.Parse("""
                            {"signedIn":true,"accountCreated":false,"returnUrl":"/dashboard",
                             "session":{"accessToken":"<access-token>","expiresIn":900,"user":{
                               "id":"0199e7a4-5c1b-7c3e-9a51-3f0f5d2b8a10","email":"user@example.test",
                               "fullName":"Người dùng mẫu","phone":null,"role":"USER","status":"ACTIVE",
                               "emailVerified":true,"hasPassword":false,"createdAt":"2026-10-15T00:00:00Z"}},
                             "linkTicket":null,"maskedEmail":null}
                            """),
                    },
                    ["linkRequired"] = new OpenApiExample
                    {
                        Summary = "Email trùng — cần xác nhận mật khẩu app, chưa cấp phiên",
                        Value = JsonNode.Parse("""
                            {"signedIn":false,"accountCreated":false,"returnUrl":"/dashboard","session":null,
                             "linkTicket":"<one-time-link-ticket>","maskedEmail":"us***@example.test"}
                            """),
                    },
                };
            }
            if (context.Description.ActionDescriptor.EndpointMetadata
                .OfType<ValidateAntiForgeryTokenAttribute>().Any())
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = AuthCsrfContract.HeaderName,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "requestToken từ GET /api/v1/auth/csrf; giữ cookie lv_antiforgery và cùng Bearer principal. Lấy proof mới sau đổi danh tính.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                });
                operation.Description += "\n\n400: validation DTO hoặc CSRF thiếu/sai (body CSRF do framework tạo, có thể không có code). 429: vượt rate limit; đọc Retry-After khi có, không tự retry POST.";
            }
            return Task.CompletedTask;
        });

        options.AddDocumentTransformer((document, context, ct) =>
        {
            // Alias vẫn chạy để tương thích; Swagger chỉ hướng dẫn prefix có cookie đúng scope.
            foreach (var alias in document.Paths.Keys.Where(path => path.StartsWith("/v1/auth/")).ToArray())
                document.Paths.Remove(alias);
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Chỉ nhập accessToken, không thêm tiền tố Bearer. Không nhập Google ID token hay refresh token. Giữ cùng trạng thái Authorize khi lấy CSRF và gửi POST.",
            };
            foreach (var (path, item) in document.Paths)
            {
                if (item.Operations is null) continue;
                // Gắn Bearer cho mọi API để nút Authorize của Swagger gửi token cho cả /vaults, /billing...
                var isAuthPath = path.StartsWith("/api/v1/auth/");
                foreach (var operation in item.Operations.Values)
                {
                    operation.Security = [new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
                    }];
                    // Public Auth vẫn nhận Bearer tùy chọn để proof và POST cùng principal.
                    if (isAuthPath && !path.EndsWith("/session")) operation.Security.Add(new OpenApiSecurityRequirement());
                }
            }
            return Task.CompletedTask;
        });
    }
}
