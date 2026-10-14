using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Tải lên và gỡ trang giấy chứng tử qua HTTP trên SQL Server thật (NFR-09, SEC-12, DEATH-04, D8).</summary>
public sealed class DeathCaseFileFlowTests : IAsyncLifetime
{
    private const string Marker = "NOI-DUNG-GIAY-CHUNG-TU-RIENG-0123456789";

    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    [SqlFact]
    public async Task Nfr09_PdfPageIsStoredEncrypted()
    {
        // Chuẩn bị
        CaseSetup setup = await CreateOpenCaseAsync();
        byte[] pdf = BuildPdf(Marker, 2048);

        // Hành động
        HttpResponseMessage response = await UploadAsync(setup, "giay chung tu.pdf", pdf);

        // Kiểm tra: response chỉ có metadata; nội dung trên đĩa đã mã hóa
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        string raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storageKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256", raw, StringComparison.OrdinalIgnoreCase);
        JsonElement body = JsonDocument.Parse(raw).RootElement;
        JsonElement file = body.GetProperty("currentVersion").GetProperty("files")[0];
        Assert.Equal("giay chung tu.pdf", file.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", file.GetProperty("contentType").GetString());
        Assert.Equal(pdf.Length, file.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(1, file.GetProperty("position").GetInt32());
        Assert.DoesNotContain("FILES", ReadStrings(body.GetProperty("missingRequirements")));

        string[] stored = Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories);
        Assert.Single(stored);
        byte[] onDisk = await File.ReadAllBytesAsync(stored[0]);
        Assert.False(onDisk.AsSpan().IndexOf(Encoding.ASCII.GetBytes(Marker)) >= 0);
        Assert.False(onDisk.AsSpan().IndexOf(Encoding.ASCII.GetBytes("%PDF-")) >= 0);
        // Không tính vào dung lượng kho (D8).
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await setup.Owner.Client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal(0, vault.GetProperty("storageUsedBytes").GetInt64());
    }

    [SqlFact]
    public async Task Ac70_Nfr09_ExeRenamedToPdfIsRejected()
    {
        // Chuẩn bị: tệp thực thi (MZ) đặt tên .pdf và khai Content-Type application/pdf
        CaseSetup setup = await CreateOpenCaseAsync();
        byte[] exe = new byte[4096];
        exe[0] = 0x4D;
        exe[1] = 0x5A;

        // Hành động
        HttpResponseMessage response = await UploadAsync(setup, "giay-chung-tu.pdf", exe);

        // Kiểm tra: 400, không có gì được ghi
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("file", out _));
        Assert.Empty(Directory.Exists(factory.StorageRoot)
            ? Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories)
            : Array.Empty<string>());
        Assert.Equal(0, await CountFilesAsync(setup.CaseId));
    }

    [SqlFact]
    public async Task Nfr09_EleventhFileIsRejected()
    {
        // Chuẩn bị: đã đủ 10 trang
        CaseSetup setup = await CreateOpenCaseAsync();
        for (int i = 0; i < 10; i++)
        {
            HttpResponseMessage ok = await UploadAsync(setup, $"trang-{i}.pdf", BuildPdf($"trang {i}", 256));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        // Hành động
        HttpResponseMessage response = await UploadAsync(setup, "trang-11.pdf", BuildPdf("trang 11", 256));

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "DEATH_CASE_FILE_LIMIT_EXCEEDED");
        Assert.Equal(10, problem.GetProperty("maxFiles").GetInt32());
        Assert.Equal(10, await CountFilesAsync(setup.CaseId));
    }

    [SqlFact]
    public async Task Nfr09_FileOver20MiBIsRejected()
    {
        // Chuẩn bị: PDF hợp lệ nhưng dài hơn 20 MiB đúng 1 byte
        CaseSetup setup = await CreateOpenCaseAsync();
        byte[] big = BuildPdf("qua lon", 20 * 1024 * 1024 + 1);

        // Hành động
        HttpResponseMessage response = await UploadAsync(setup, "qua-lon.pdf", big);

        // Kiểm tra: 413, không để lại nội dung mồ côi
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE");
        Assert.Equal(0, await CountFilesAsync(setup.CaseId));
        string[] stored = Directory.Exists(factory.StorageRoot)
            ? Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories)
            : Array.Empty<string>();
        Assert.Empty(stored);
    }

    [SqlFact]
    public async Task Death04_CannotDeleteFileOfSubmittedVersion()
    {
        // Chuẩn bị: có một trang, rồi bản này được đánh dấu đã xác nhận gửi (đặt bằng SQL)
        CaseSetup setup = await CreateOpenCaseAsync();
        JsonElement uploaded = await ApiTestHelpers.ReadJsonAsync(
            await UploadAsync(setup, "trang.pdf", BuildPdf("trang", 512)));
        Guid fileId = uploaded.GetProperty("currentVersion").GetProperty("files")[0].GetProperty("id").GetGuid();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseVersion>().Where(v => v.CaseId == setup.CaseId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.SubmittedAt, DateTimeOffset.UtcNow));
        });

        // Hành động
        HttpResponseMessage delete = await setup.Executor.Client.DeleteAsync(FileUrl(setup.CaseId, fileId));
        HttpResponseMessage upload = await UploadAsync(setup, "trang-2.pdf", BuildPdf("trang 2", 512));

        // Kiểm tra: bản đã gửi không đổi
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(delete, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("VERSION_LOCKED", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.AssertProblemAsync(upload, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal(1, await CountFilesAsync(setup.CaseId));
        Assert.Single(Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories));
    }

    [SqlFact]
    public async Task Death01_DeleteDraftFileRemovesContent()
    {
        // Chuẩn bị: hai trang
        CaseSetup setup = await CreateOpenCaseAsync();
        JsonElement first = await ApiTestHelpers.ReadJsonAsync(
            await UploadAsync(setup, "trang-1.pdf", BuildPdf("trang 1", 512)));
        await UploadAsync(setup, "trang-2.pdf", BuildPdf("trang 2", 512));
        Guid firstFileId = first.GetProperty("currentVersion").GetProperty("files")[0].GetProperty("id").GetGuid();
        Assert.Equal(2, Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories).Length);

        // Hành động
        HttpResponseMessage delete = await setup.Executor.Client.DeleteAsync(FileUrl(setup.CaseId, firstFileId));
        HttpResponseMessage deleteAgain = await setup.Executor.Client.DeleteAsync(FileUrl(setup.CaseId, firstFileId));

        // Kiểm tra: còn 1 trang, nội dung của trang đã gỡ bị xóa khỏi kho lưu trữ
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(delete);
        JsonElement files = body.GetProperty("currentVersion").GetProperty("files");
        Assert.Equal(1, files.GetArrayLength());
        Assert.Equal("trang-2.pdf", files[0].GetProperty("fileName").GetString());
        Assert.Single(Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories));
        Assert.Equal(1, await CountFilesAsync(setup.CaseId));
        await ApiTestHelpers.AssertProblemAsync(deleteAgain, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
    }

    private async Task<CaseSetup> CreateOpenCaseAsync()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        HttpResponseMessage response = await executor.Client.PostAsync(
            $"/api/v1/executor/vaults/{vault.VaultId}/death-case", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);

        CaseSetup setup = new CaseSetup();
        setup.Owner = vault;
        setup.Executor = executor;
        setup.CaseId = body.GetProperty("id").GetGuid();
        return setup;
    }

    private static Task<HttpResponseMessage> UploadAsync(CaseSetup setup, string fileName, byte[] content)
    {
        ByteArrayContent fileContent = new ByteArrayContent(content);
        // Content-Type khai báo luôn là PDF để chứng minh BE không tin nó.
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(fileContent, "file", fileName);
        return setup.Executor.Client.PostAsync($"/api/v1/executor/death-cases/{setup.CaseId}/files", form);
    }

    private static string FileUrl(Guid caseId, Guid fileId)
    {
        return $"/api/v1/executor/death-cases/{caseId}/files/{fileId}";
    }

    /// <summary>PDF giả hợp lệ về chữ ký đầu tệp: "%PDF-1.7", chuỗi marker, rồi đệm tới đủ độ dài.</summary>
    private static byte[] BuildPdf(string marker, int length)
    {
        byte[] result = new byte[length];
        byte[] head = Encoding.ASCII.GetBytes("%PDF-1.7\n" + marker + "\n");
        Array.Copy(head, result, Math.Min(head.Length, length));
        for (int i = head.Length; i < length; i++)
        {
            result[i] = (byte)'A';
        }
        return result;
    }

    private static List<string> ReadStrings(JsonElement array)
    {
        List<string> values = new List<string>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            values.Add(item.GetString()!);
        }
        return values;
    }

    private async Task<int> CountFilesAsync(Guid caseId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.Set<DeathCaseFile>()
                .CountAsync(f => db.Set<DeathCaseVersion>().Any(v => v.Id == f.VersionId && v.CaseId == caseId));
        });
        return count;
    }

    private sealed class CaseSetup
    {
        public DeathCaseTestData.OwnerVault Owner { get; set; } = null!;

        public DeathCaseTestData.ExecutorClient Executor { get; set; } = null!;

        public Guid CaseId { get; set; }
    }
}
