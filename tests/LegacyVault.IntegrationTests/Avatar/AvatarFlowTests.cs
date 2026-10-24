using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Avatar;

/// <summary>
/// Ảnh đại diện qua HTTP trên SQL Server thật (api-contract mục 6.4a). Cách chạy giống AssetManagementFlowTests:
/// [SqlFact] cần LEGACYVAULT_TEST_CONNECTION, mỗi lớp test dùng một database tạm.
/// </summary>
public sealed class AvatarFlowTests : IAsyncLifetime
{
    private const string AvatarUrl = "/api/v1/me/avatar";

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
    public async Task UploadThenDownloadReturnsSameImage()
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        HttpResponseMessage none = await client.GetAsync(AvatarUrl);
        await ApiTestHelpers.AssertProblemAsync(none, HttpStatusCode.NotFound, "AVATAR_NOT_FOUND");

        HttpResponseMessage put = await client.PutAsync(AvatarUrl, Form(PngBytes, "image/png", "a.png"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        JsonElement user = await ApiTestHelpers.ReadJsonAsync(put);
        Assert.True(user.GetProperty("hasAvatar").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, user.GetProperty("avatarUpdatedAt").ValueKind);

        HttpResponseMessage get = await client.GetAsync(AvatarUrl);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", get.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(PngBytes, await get.Content.ReadAsByteArrayAsync());
    }

    [SqlFact]
    public async Task TypeIsDetectedFromContentNotFromDeclaredType()
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Khai báo image/png nhưng nội dung là văn bản: bị từ chối.
        byte[] text = System.Text.Encoding.UTF8.GetBytes("khong phai anh");
        HttpResponseMessage fake = await client.PutAsync(AvatarUrl, Form(text, "image/png", "a.png"));
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(fake, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Contains("JPEG", problem.GetProperty("errors").GetProperty("file")[0].GetString());

        // PDF thật cũng không nhận (chỉ JPEG/PNG/WebP).
        byte[] pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 gia");
        HttpResponseMessage notImage = await client.PutAsync(AvatarUrl, Form(pdf, "application/pdf", "a.pdf"));
        await ApiTestHelpers.AssertProblemAsync(notImage, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        HttpResponseMessage get = await client.GetAsync(AvatarUrl);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [SqlFact]
    public async Task ImageOverTwoMiBIsRejected()
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        byte[] big = new byte[(int)AvatarRules.MaxBytes + 1];
        Array.Copy(PngBytes, big, PngBytes.Length);

        HttpResponseMessage response = await client.PutAsync(AvatarUrl, Form(big, "image/png", "big.png"));

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task ReplacingKeepsOnlyTheNewImageOnDisk()
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(AvatarUrl, Form(PngBytes, "image/png", "a.png"))).StatusCode);
        Assert.Single(Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories));

        byte[] second = BuildPng(500);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(AvatarUrl, Form(second, "image/png", "b.png"))).StatusCode);

        Assert.Single(Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories));
        Assert.Equal(second, await client.GetByteArrayAsync(AvatarUrl));
    }

    [SqlFact]
    public async Task DeleteRemovesImageAndIsIdempotent()
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        await client.PutAsync(AvatarUrl, Form(PngBytes, "image/png", "a.png"));

        HttpResponseMessage deleted = await client.DeleteAsync(AvatarUrl);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        JsonElement user = await ApiTestHelpers.ReadJsonAsync(deleted);
        Assert.False(user.GetProperty("hasAvatar").GetBoolean());
        Assert.Empty(Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AvatarUrl)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(AvatarUrl)).StatusCode);
    }

    [SqlFact]
    public async Task EachUserOnlySeesOwnAvatarAndAnonymousIsRejected()
    {
        HttpClient first = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        HttpClient second = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        await first.PutAsync(AvatarUrl, Form(PngBytes, "image/png", "a.png"));

        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync(AvatarUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync(AvatarUrl)).StatusCode);

        HttpClient anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(AvatarUrl)).StatusCode);
    }

    [SqlFact]
    public async Task StoredKeyIsWrappedAndNotReturnedToClient()
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        HttpResponseMessage put = await client.PutAsync(AvatarUrl, Form(PngBytes, "image/png", "a.png"));
        string body = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storageKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wrapped", body, StringComparison.OrdinalIgnoreCase);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            User user = await db.Users.SingleAsync(u => u.AvatarStorageKey != null);
            Assert.Equal(60, user.AvatarWrappedDataKey!.Length);
            Assert.Equal("image/png", user.AvatarContentType);
            Assert.Equal(PngBytes.Length, user.AvatarSizeBytes);
        });
    }

    [Fact]
    public void RulesAcceptOnlyJpegPngWebp()
    {
        Assert.Equal("image/jpeg", AvatarRules.DetectImageContentType(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.Equal("image/png", AvatarRules.DetectImageContentType(PngBytes.Take(12).ToArray()));
        byte[] webp = { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 };
        Assert.Equal("image/webp", AvatarRules.DetectImageContentType(webp));
        Assert.Null(AvatarRules.DetectImageContentType(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 xxxxxx")));
        Assert.Null(AvatarRules.DetectImageContentType(new byte[12]));
    }

    private static MultipartFormDataContent Form(byte[] bytes, string contentType, string fileName)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        ByteArrayContent file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return form;
    }

    /// <summary>Tệp có chữ ký PNG thật theo sau là dữ liệu tùy ý với độ dài cho trước (đủ để nhận loại ảnh).</summary>
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
