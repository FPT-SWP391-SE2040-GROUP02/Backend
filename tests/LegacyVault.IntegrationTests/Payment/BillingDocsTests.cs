using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// Chống tài liệu thanh toán lệch code: docs/api-contract.md và http/billing.http phải nhắc đủ endpoint,
/// mã lỗi, lý do lỗi và loại nhật ký đang có (SRS §8). Không cần database.
/// </summary>
public sealed class BillingDocsTests
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
    public void Sec83_ApiContract_DocumentsEveryBillingEndpoint()
    {
        // Chuẩn bị
        string contract = ReadRepoFile("docs", "api-contract.md");
        string[] endpoints = new string[]
        {
            "/api/v1/billing/plans",
            "/api/v1/billing/quote",
            "/api/v1/billing/orders",
            "/api/v1/billing/orders/current",
            "/api/v1/billing/orders/{orderId}",
            "/api/v1/billing/orders/{orderId}/cancel",
            "/api/v1/billing/orders/{orderId}/dev-pay",
        };

        // Hành động / Kiểm tra
        foreach (string endpoint in endpoints)
        {
            Assert.Contains("`" + endpoint + "`", contract);
        }
    }

    [Fact]
    public void Sec83_ApiContract_DocumentsBillingErrorCodesAndReasons()
    {
        // Chuẩn bị
        string contract = ReadRepoFile("docs", "api-contract.md");
        string[] codesAndReasons = new string[]
        {
            "PAYMENT_NOT_ALLOWED",
            "PAYMENT_ORDER_PENDING",
            "PAYMENT_ORDER_NOT_PAYABLE",
            "PAYMENT_ORDER_NOT_FOUND",
            "OWNER_EKYC_REQUIRED",
            "RENEWAL_NOT_SUPPORTED",
            "PLAN_LOCKED",
            "ENTITLEMENT_CHANGED",
            "CANCELLED",
            "VAULT_CLOSED",
        };

        // Hành động / Kiểm tra
        foreach (string text in codesAndReasons)
        {
            Assert.Contains("`" + text + "`", contract);
        }
    }

    [Fact]
    public void Sec83_ApiContract_DocumentsBillingAuditActions()
    {
        // Chuẩn bị
        string contract = ReadRepoFile("docs", "api-contract.md");

        // Hành động / Kiểm tra
        Assert.Contains("`PAYMENT_ORDER_CREATED`", contract);
        Assert.Contains("`PAYMENT_ORDER_CANCELLED`", contract);
        Assert.Contains("`PAYMENT_PAID`", contract);
    }

    [Fact]
    public void Sec83_BillingHttpFile_HasEveryRequestAndNoRealToken()
    {
        // Chuẩn bị
        string http = ReadRepoFile("http", "billing.http");
        string[] requests = new string[]
        {
            "GET {{baseUrl}}/billing/plans",
            "POST {{baseUrl}}/billing/quote",
            "POST {{baseUrl}}/billing/orders",
            "GET {{baseUrl}}/billing/orders/current",
            "GET {{baseUrl}}/billing/orders/{{orderId}}",
            "POST {{baseUrl}}/billing/orders/{{orderId}}/cancel",
            "POST {{baseUrl}}/billing/orders/{{orderId}}/dev-pay",
        };

        // Hành động / Kiểm tra
        foreach (string request in requests)
        {
            Assert.Contains(request, http);
        }
        Assert.Contains("@token =\n", http.Replace("\r\n", "\n"));
        Assert.DoesNotContain("eyJ", http);
    }
}
