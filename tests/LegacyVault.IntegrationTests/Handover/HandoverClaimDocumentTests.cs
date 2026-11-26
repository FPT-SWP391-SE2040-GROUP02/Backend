using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Giấy tờ của yêu cầu nhận tài sản (cùng luật với hồ sơ báo tin): bắt buộc giấy chứng tử và di chúc, lưu theo loại;
/// tệp lưu kiểu cũ vẫn đọc được.
/// </summary>
public sealed class HandoverClaimDocumentTests : IAsyncLifetime
{
    private const string RequestsUrl = "/api/v1/handover/requests";

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

    private async Task<(HttpClient Recipient, Guid VaultId)> BuildRecipientAsync()
    {
        string email = "giayto-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@example.com";
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000041", ApiTestHelpers.BeneficiaryName(0), email);
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, assetId, beneficiary.GetProperty("id").GetGuid());
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        HttpClient recipient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, email);
        return (recipient, vaultId);
    }

    private static MultipartFormDataContent Form(Guid vaultId, bool certificate, bool will, bool other)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(new StringContent(vaultId.ToString()), "vaultId");
        if (certificate)
        {
            form.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "deathCertificates", "chung-tu.pdf");
        }
        if (will)
        {
            form.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "wills", "di-chuc.pdf");
        }
        if (other)
        {
            form.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "files", "khac.pdf");
        }
        return form;
    }

    private static List<UploadedFile> Files(int count)
    {
        List<UploadedFile> result = new List<UploadedFile>();
        for (int index = 0; index < count; index++)
        {
            UploadedFile file = new UploadedFile();
            file.FileName = "tep-" + index + ".pdf";
            file.Content = HandoverRecordTestData.SamplePdf();
            result.Add(file);
        }
        return result;
    }

    [Fact]
    public void ValidateFirstSubmission_AllowsAtMostTenFilesInTotal()
    {
        // Chuẩn bị
        HandoverDocumentUploads ten = new HandoverDocumentUploads();
        ten.DeathCertificates = Files(1);
        ten.Wills = Files(1);
        ten.Others = Files(8);
        HandoverDocumentUploads eleven = new HandoverDocumentUploads();
        eleven.DeathCertificates = Files(1);
        eleven.Wills = Files(1);
        eleven.Others = Files(9);

        // Hành động
        HandoverDocumentRules.ValidateFirstSubmission(ten);
        AppException error = Assert.Throws<AppException>(() => HandoverDocumentRules.ValidateFirstSubmission(eleven));

        // Kiểm tra
        Dictionary<string, string[]> errors = Assert.IsType<Dictionary<string, string[]>>(error.Extensions["errors"]);
        Assert.True(errors.ContainsKey("files"));
    }

    [SqlFact]
    public async Task Submit_WithoutARequiredDocument_Returns400OnThatField()
    {
        // Chuẩn bị
        (HttpClient recipient, Guid vaultId) = await BuildRecipientAsync();

        // Hành động
        HttpResponseMessage noCertificate = await recipient.PostAsync(
            RequestsUrl + "/by-account", Form(vaultId, certificate: false, will: true, other: true));
        HttpResponseMessage noWill = await recipient.PostAsync(
            RequestsUrl + "/by-account", Form(vaultId, certificate: true, will: false, other: true));

        // Kiểm tra: đúng trường, chưa tạo biên bản
        JsonElement first = await ApiTestHelpers.AssertProblemAsync(noCertificate, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(first.GetProperty("errors").TryGetProperty("deathCertificates", out _));
        JsonElement second = await ApiTestHelpers.AssertProblemAsync(noWill, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(second.GetProperty("errors").TryGetProperty("wills", out _));
        int records = 1;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            records = await db.Set<HandoverRecord>().CountAsync(r => r.VaultId == vaultId);
        });
        Assert.Equal(0, records);
    }

    [SqlFact]
    public async Task Submit_StoresEachKind_AndOldUnclassifiedFilesStillShow()
    {
        // Chuẩn bị
        (HttpClient recipient, Guid vaultId) = await BuildRecipientAsync();

        // Hành động: gửi đủ ba loại
        HttpResponseMessage response = await recipient.PostAsync(
            RequestsUrl + "/by-account", Form(vaultId, certificate: true, will: true, other: true));
        JsonElement claim = await ApiTestHelpers.ReadJsonAsync(response);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<string?> kinds = new List<string?>();
        foreach (JsonElement file in claim.GetProperty("files").EnumerateArray())
        {
            kinds.Add(file.GetProperty("kind").GetString());
        }
        Assert.Equal(new string?[] { "DEATH_CERTIFICATE", "WILL", "OTHER" }, kinds);

        // Chuẩn bị: giả lập yêu cầu lưu từ trước khi chia loại
        Guid claimId = claim.GetProperty("id").GetGuid();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<HandoverRecordFile> files = await db.Set<HandoverRecordFile>().Where(f => f.ClaimId == claimId).ToListAsync();
            foreach (HandoverRecordFile file in files)
            {
                file.Kind = HandoverRecordFileKind.ClaimDocument;
            }
            await db.SaveChangesAsync();
        });

        // Hành động
        JsonElement mine = (await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync(RequestsUrl + "/mine")))[0];

        // Kiểm tra
        Assert.Equal(3, mine.GetProperty("files").GetArrayLength());
        Assert.Equal("CLAIM_DOCUMENT", mine.GetProperty("files")[0].GetProperty("kind").GetString());
    }
}
