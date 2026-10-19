using System.Text.RegularExpressions;
using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// Chống tài liệu webhook SePay lệch code: docs/api-contract.md phải nhắc đủ endpoint, mã lỗi, lý do đối chiếu,
/// loại sự cố và cấu hình; http/billing-webhook.http không chứa key (SRS §8.5, ADMIN-09). Không cần database.
/// </summary>
public sealed class SePayDocsTests
{
    private static string ReadRepoFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, Path.Combine(relativeParts));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Không tìm thấy " + Path.Combine(relativeParts));
    }

    [Fact]
    public void Pay05_ApiContract_DocumentsWebhookAndAdminEndpoints()
    {
        // Chuẩn bị
        string contract = ReadRepoFile("docs", "api-contract.md");
        string[] endpoints = new string[]
        {
            "/api/v1/billing/sepay/webhook",
            "/api/v1/admin/payment-transactions",
        };

        // Hành động / Kiểm tra
        foreach (string endpoint in endpoints)
        {
            Assert.Contains("`" + endpoint, contract);
        }
    }

    [Fact]
    public void Pay07_ApiContract_DocumentsEveryReconciliationReason()
    {
        // Chuẩn bị
        string contract = ReadRepoFile("docs", "api-contract.md");
        string[] reasons = new string[]
        {
            PaymentReconciliationReasons.NotIncoming,
            PaymentReconciliationReasons.WrongAccount,
            PaymentReconciliationReasons.CodeMissing,
            PaymentReconciliationReasons.OrderNotFound,
            PaymentReconciliationReasons.AmountMismatch,
            PaymentReconciliationReasons.AlreadyPaid,
            PaymentReconciliationReasons.OrderCancelled,
            PaymentReconciliationReasons.TimeUnknown,
            PaymentReconciliationReasons.LateAfterExpiry,
        };

        // Hành động / Kiểm tra
        foreach (string reason in reasons)
        {
            Assert.Contains("`" + reason + "`", contract);
        }
    }

    [Fact]
    public void Pay05_ApiContract_DocumentsErrorCodeIncidentTypeStatusesAndConfiguration()
    {
        // Chuẩn bị
        string contract = ReadRepoFile("docs", "api-contract.md");
        string[] expected = new string[]
        {
            "WEBHOOK_UNAUTHORIZED",
            "`PAYMENT`",
            "RECEIVED",
            "MATCHED",
            "RECONCILIATION_REQUIRED",
            "RESOLVED",
            "Billing:SePay:WebhookApiKey",
            "SePay:Environment",
            "Authorization: Apikey",
            "{\"success\":true}",
        };

        // Hành động / Kiểm tra
        foreach (string text in expected)
        {
            Assert.Contains(text, contract);
        }
    }

    [Fact]
    public void Sec07_WebhookHttpFile_HasEmptyApiKeyAndNoKeyLikeSecret()
    {
        // Chuẩn bị
        string http = ReadRepoFile("http", "billing-webhook.http");

        // Hành động
        bool hasEmptyApiKey = Regex.IsMatch(http, @"^@apiKey =\s*$", RegexOptions.Multiline);
        bool hasEmptyAdminToken = Regex.IsMatch(http, @"^@adminToken =\s*$", RegexOptions.Multiline);
        MatchCollection authorizationLines = Regex.Matches(http, @"^Authorization: Apikey (\S+)\s*$", RegexOptions.Multiline);

        // Kiểm tra: key để trống; mọi dòng gửi key dùng biến hoặc giá trị rõ ràng là giả
        Assert.True(hasEmptyApiKey);
        Assert.True(hasEmptyAdminToken);
        Assert.NotEmpty(authorizationLines);
        foreach (Match line in authorizationLines)
        {
            string value = line.Groups[1].Value;
            bool isVariable = value == "{{apiKey}}";
            bool isFake = value == "khong-phai-key-that";
            Assert.True(isVariable || isFake, "Dòng Authorization chứa giá trị lạ: " + value);
        }
    }

    [Fact]
    public void Sec07_ReadmeAndContract_DoNotContainWebhookKeyValues()
    {
        // Chuẩn bị
        string readme = ReadRepoFile("README.md");
        string contract = ReadRepoFile("docs", "api-contract.md");

        // Hành động: mọi chỗ đặt key trong tài liệu phải là chỗ giữ chỗ <key>
        MatchCollection setCommands = Regex.Matches(
            readme + contract, @"Billing:SePay:WebhookApiKey""\s+""([^""]*)""");

        // Kiểm tra
        Assert.NotEmpty(setCommands);
        foreach (Match command in setCommands)
        {
            Assert.Equal("<key>", command.Groups[1].Value);
        }
    }
}
