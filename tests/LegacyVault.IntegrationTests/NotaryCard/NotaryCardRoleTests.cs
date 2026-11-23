using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.NotaryCard;

/// <summary>
/// Ảnh thẻ chỉ dành cho Người xét duyệt (luật sư); Người thực thi là nhân viên hệ thống nên không có ảnh thẻ.
/// Danh bạ Người thực thi không còn trường legalVerification. Chạy trên SQL Server thật ([SqlFact]).
/// </summary>
public sealed class NotaryCardRoleTests : IAsyncLifetime
{
    private const string VerifierCardUrl = "/api/v1/verifier/notary-card";

    private static readonly byte[] PngBytes = BuildPng(300);

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
    public async Task Executor_GetStatusAndDownload_ReturnNotFound()
    {
        // Chuẩn bị
        User executor = await VaultTestData.AddUserAsync(factory, UserRole.Executor, UserStatus.Active);
        HttpClient executorClient = ClientFor(executor);

        // Hành động
        HttpResponseMessage status = await executorClient.GetAsync(VerifierCardUrl);
        HttpResponseMessage download = await executorClient.GetAsync(VerifierCardUrl + "/content");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(status, HttpStatusCode.NotFound, "USER_NOT_FOUND");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [SqlFact]
    public async Task Admin_ViewExecutorCard_ReturnsNotFound()
    {
        // Chuẩn bị
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User executor = await VaultTestData.AddUserAsync(factory, UserRole.Executor, UserStatus.Active);
        HttpClient adminClient = ClientFor(admin);

        // Hành động
        HttpResponseMessage response = await adminClient.GetAsync($"/api/v1/admin/users/{executor.Id}/notary-card");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "USER_NOT_FOUND");
    }

    [SqlFact]
    public async Task Admin_UploadCardForExecutor_IsRejected_AndLegalVerifiedAtUnchanged()
    {
        // Chuẩn bị
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User executor = await VaultTestData.AddUserAsync(factory, UserRole.Executor, UserStatus.Active);
        HttpClient adminClient = ClientFor(admin);

        // Hành động
        HttpResponseMessage response = await adminClient.PutAsync(
            $"/api/v1/admin/users/{executor.Id}/notary-card", Form(PngBytes));

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        User? reloaded = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            reloaded = await db.Users.FindAsync(executor.Id);
        });
        Assert.NotNull(reloaded);
        Assert.Null(reloaded.NotaryCardStorageKey);
        Assert.Null(reloaded.LegalVerifiedAt);
    }

    [SqlFact]
    public async Task Verifier_StillUploadedByAdmin_AndCanReadOwnCard()
    {
        // Chuẩn bị
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User verifier = await VaultTestData.AddVerifierAsync(factory);
        HttpClient adminClient = ClientFor(admin);
        HttpClient verifierClient = ClientFor(verifier);

        // Hành động
        HttpResponseMessage upload = await adminClient.PutAsync(
            $"/api/v1/admin/users/{verifier.Id}/notary-card", Form(PngBytes));
        HttpResponseMessage status = await verifierClient.GetAsync(VerifierCardUrl);
        HttpResponseMessage download = await verifierClient.GetAsync(VerifierCardUrl + "/content");
        HttpResponseMessage adminView = await adminClient.GetAsync($"/api/v1/admin/users/{verifier.Id}/notary-card");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(status);
        Assert.True(body.GetProperty("hasCard").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(PngBytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, adminView.StatusCode);
    }

    [SqlFact]
    public async Task SystemExecutorDirectory_NoLongerReturnsLegalVerification()
    {
        // Chuẩn bị
        await ApiTestHelpers.EnsureSystemExecutorAsync(factory, "nhan-vien-thu@example.test");
        HttpClient ownerClient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        await ApiTestHelpers.CreateVaultAsync(ownerClient);

        // Hành động
        HttpResponseMessage response = await ownerClient.GetAsync("/api/v1/system-executors");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement list = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.True(list.GetArrayLength() >= 1);
        foreach (JsonElement item in list.EnumerateArray())
        {
            Assert.False(item.TryGetProperty("legalVerification", out JsonElement _));
            Assert.True(item.TryGetProperty("fullName", out JsonElement _));
        }
    }

    private HttpClient ClientFor(User user)
    {
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);
        return client;
    }

    private static MultipartFormDataContent Form(byte[] bytes)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        ByteArrayContent file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "card.png");
        return form;
    }

    /// <summary>Tệp có chữ ký PNG thật theo sau là dữ liệu tùy ý (đủ để nhận loại ảnh).</summary>
    private static byte[] BuildPng(int length)
    {
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        byte[] bytes = new byte[length];
        Array.Copy(signature, bytes, signature.Length);
        for (int i = signature.Length; i < length; i++)
        {
            bytes[i] = (byte)(i * 31 % 251);
        }
        return bytes;
    }
}
