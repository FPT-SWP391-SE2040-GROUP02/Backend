using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Hàm gọi API hồ sơ chứng tử dùng chung cho các test luồng gửi hồ sơ.</summary>
public static class DeathCaseApi
{
    public const string CitizenId = "012345678901";

    public static string OpenUrl(Guid vaultId)
    {
        return $"/api/v1/executor/vaults/{vaultId}/death-case";
    }

    public static string CaseUrl(Guid caseId)
    {
        return $"/api/v1/executor/death-cases/{caseId}";
    }

    /// <summary>Ngày hôm nay theo giờ Việt Nam, giống cách service tính.</summary>
    public static DateOnly Today()
    {
        return DeathCaseRules.TodayInVietnam(DateTimeOffset.UtcNow);
    }

    public static string Format(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd");
    }

    /// <summary>Mở hồ sơ (phải là lần mở đầu, 201), trả JSON hồ sơ.</summary>
    public static async Task<JsonElement> OpenAsync(HttpClient client, Guid vaultId)
    {
        HttpResponseMessage response = await client.PostAsync(OpenUrl(vaultId), null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    /// <summary>Khai đủ 6 trường vào bản nháp (PUT .../draft, 200), trả JSON hồ sơ mới.</summary>
    public static async Task<JsonElement> FillDraftAsync(
        HttpClient client, JsonElement caseBody, DateOnly dateOfDeath, string ownerFullName = "Nguyễn Văn An")
    {
        Guid caseId = caseBody.GetProperty("id").GetGuid();
        HttpResponseMessage response = await client.PutAsJsonAsync(CaseUrl(caseId) + "/draft", new
        {
            etag = caseBody.GetProperty("etag").GetString(),
            ownerFullName,
            ownerCitizenId = CitizenId,
            dateOfDeath = Format(dateOfDeath),
            certificateNumber = "123/2026/TT",
            issuedOn = Format(dateOfDeath.AddDays(1)),
            issuedBy = "UBND phường Bến Nghé",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    /// <summary>Tải một trang PDF giả (201), trả JSON hồ sơ mới.</summary>
    public static async Task<JsonElement> UploadPdfAsync(HttpClient client, Guid caseId, string marker)
    {
        byte[] content = Encoding.ASCII.GetBytes("%PDF-1.7\n" + marker + "\n" + new string('A', 512));
        ByteArrayContent fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(fileContent, "file", "trang.pdf");
        HttpResponseMessage response = await client.PostAsync(CaseUrl(caseId) + "/files", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    /// <summary>Mở hồ sơ rồi khai đủ trường và tải một trang: hồ sơ sẵn sàng để gửi. Trả JSON hồ sơ mới nhất.</summary>
    public static async Task<JsonElement> PrepareCompleteDraftAsync(HttpClient client, Guid vaultId, DateOnly dateOfDeath)
    {
        JsonElement opened = await OpenAsync(client, vaultId);
        JsonElement filled = await FillDraftAsync(client, opened, dateOfDeath);
        return await UploadPdfAsync(client, filled.GetProperty("id").GetGuid(), "trang chung tu");
    }

    public static Task<HttpResponseMessage> SubmitAsync(HttpClient client, Guid caseId, string? etag, bool confirm = true)
    {
        return client.PostAsJsonAsync(CaseUrl(caseId) + "/submit", new { etag, confirm });
    }

    /// <summary>Gửi hồ sơ (phải thành công, 200), trả JSON hồ sơ.</summary>
    public static async Task<JsonElement> SubmitOkAsync(HttpClient client, JsonElement caseBody)
    {
        HttpResponseMessage response = await SubmitAsync(
            client, caseBody.GetProperty("id").GetGuid(), caseBody.GetProperty("etag").GetString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    public static List<string> ReadStrings(JsonElement array)
    {
        List<string> values = new List<string>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            values.Add(item.GetString()!);
        }
        return values;
    }
}
