using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// CRUD hồ sơ người nhận cấp kho qua HTTP trên SQL Server thật (docs/api-contract.md mục 6.8).
/// </summary>
/// <remarks>
/// Cách chạy giống ContainerFlowTests: [SqlFact] chỉ chạy khi có LEGACYVAULT_TEST_CONNECTION; mỗi lớp test dùng
/// một database tạm. Trừ ca kiểm hạng Free, các test nâng kho lên XS bằng cách sửa thẳng DB (chưa có thanh toán).
/// </remarks>
public sealed class BeneficiaryFlowTests : IAsyncLifetime
{
    private const string LeadingZeroCitizenId = "012345678901";
    private const string SameLast4CitizenId = "999999998901";
    private const string ThirdCitizenId = "123456789012";
    private const string FullName = "Nguyễn Văn An";
    private const string Email = ApiTestHelpers.BeneficiaryEmail;

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
    public async Task Ac04_Setup17_FreeVaultCannotCreateBeneficiary()
    {
        // Chuẩn bị: kho mới luôn ở hạng Free.
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(LeadingZeroCitizenId, FullName));

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "PAID_TIER_REQUIRED");
        Assert.Equal(0, await CountBeneficiariesAsync(vaultId));
    }

    [SqlFact]
    public async Task Ekyc08_CitizenIdMustBeTwelveDigits()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);

        // Hành động
        HttpResponseMessage elevenDigits = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody("01234567890", FullName));
        HttpResponseMessage withLetter = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody("01234567890A", FullName));

        // Kiểm tra
        JsonElement first = await ApiTestHelpers.AssertProblemAsync(elevenDigits, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(first.GetProperty("errors").TryGetProperty("citizenId", out _));
        await ApiTestHelpers.AssertProblemAsync(withLetter, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(0, await CountBeneficiariesAsync(vaultId));
    }

    [SqlFact]
    public async Task Section52Step7_MissingBothEmailAndPhoneIsRejected()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);

        // Hành động: email "" và phone chỉ khoảng trắng đều coi là không có.
        HttpResponseMessage response = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = "1990-05-20", email = "", phone = "  " });

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task Beneficiary_FutureDateOfBirthIsRejected()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        string tomorrow = TodayInVietnam().AddDays(1).ToString("yyyy-MM-dd");

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = tomorrow, email = Email });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("dateOfBirth", out _));
    }

    [SqlFact]
    public async Task Ekyc08_Quota04_LeadingZeroKeptAndResubmitReturnsSameProfile()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);

        // Hành động
        HttpResponseMessage first = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(LeadingZeroCitizenId, FullName));
        HttpResponseMessage again = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(LeadingZeroCitizenId, FullName));

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.NotNull(first.Headers.Location);
        JsonElement created = await ApiTestHelpers.ReadJsonAsync(first);
        Assert.Equal(LeadingZeroCitizenId, created.GetProperty("citizenId").GetString());
        Assert.Equal(FullName, created.GetProperty("fullName").GetString());
        Assert.Equal("1990-05-20", created.GetProperty("dateOfBirth").GetString());
        Assert.Equal(Email, created.GetProperty("email").GetString());
        Assert.Equal(0, created.GetProperty("assetIds").GetArrayLength());
        Assert.Equal("\"0\"", created.GetProperty("etag").GetString());

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        JsonElement existing = await ApiTestHelpers.ReadJsonAsync(again);
        Assert.Equal(created.GetProperty("id").GetGuid(), existing.GetProperty("id").GetGuid());
        // Lần gửi thứ hai khớp được nghĩa là số giải mã từ DB vẫn đủ 12 số, giữ số 0 đầu.
        Assert.Equal(1, await CountBeneficiariesAsync(vaultId));
    }

    [SqlFact]
    public async Task Quota04_DifferentCitizenIdsWithSameLast4AreSeparateProfiles()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);

        // Hành động
        HttpResponseMessage first = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(LeadingZeroCitizenId, FullName));
        HttpResponseMessage second = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(SameLast4CitizenId, "Trần Thị Bình"));

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Guid firstId = (await ApiTestHelpers.ReadJsonAsync(first)).GetProperty("id").GetGuid();
        Guid secondId = (await ApiTestHelpers.ReadJsonAsync(second)).GetProperty("id").GetGuid();
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(2, await CountBeneficiariesAsync(vaultId));
    }

    [SqlFact]
    public async Task Match02_SameCitizenIdWithNameDifferingOnlyInCaseAndSpacingReturnsExisting()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement created = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            AdultBody("0123 4567 8901", "  nguyễn   VĂN  an "));

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement existing = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(created.GetProperty("id").GetGuid(), existing.GetProperty("id").GetGuid());
        Assert.Equal(FullName, existing.GetProperty("fullName").GetString());
        Assert.Equal(1, await CountBeneficiariesAsync(vaultId));
    }

    [SqlFact]
    public async Task Quota04_Ac92_SameCitizenIdWithDifferentNameOrBirthDateConflicts()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement created = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        Guid createdId = created.GetProperty("id").GetGuid();

        // Hành động
        HttpResponseMessage otherName = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            AdultBody(LeadingZeroCitizenId, "Nguyễn Văn Ân"));
        HttpResponseMessage otherBirthDate = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = "1990-05-21", email = Email });

        // Kiểm tra
        JsonElement nameProblem = await ApiTestHelpers.AssertProblemAsync(
            otherName, HttpStatusCode.Conflict, "BENEFICIARY_IDENTITY_CONFLICT");
        Assert.Equal(createdId, nameProblem.GetProperty("beneficiaryId").GetGuid());
        JsonElement birthProblem = await ApiTestHelpers.AssertProblemAsync(
            otherBirthDate, HttpStatusCode.Conflict, "BENEFICIARY_IDENTITY_CONFLICT");
        Assert.Equal(createdId, birthProblem.GetProperty("beneficiaryId").GetGuid());
        Assert.Equal(1, await CountBeneficiariesAsync(vaultId));
    }

    [SqlFact]
    public async Task Beneficiary_UnderEighteenIsRejected()
    {
        // Chuẩn bị: người nhận 10 tuổi.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        string tenYearsAgo = TodayInVietnam().AddYears(-10).ToString("yyyy-MM-dd");

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = tenYearsAgo, email = Email, phone = "0901 234.567" });

        // Kiểm tra: 400 ở trường dateOfBirth và không tạo hồ sơ nào.
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("dateOfBirth", out _));
        Assert.Equal(0, await CountBeneficiariesAsync(vaultId));
    }

    [SqlFact]
    public async Task Beneficiary_TurningEighteenTodayIsAccepted()
    {
        // Chuẩn bị: hôm nay đúng tròn 18 tuổi (biên được phép).
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        string exactlyEighteen = TodayInVietnam().AddYears(-18).ToString("yyyy-MM-dd");

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = exactlyEighteen, email = Email, phone = "0901 234.567" });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement created = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.False(created.TryGetProperty("isMinor", out _));
        Assert.Equal("0901234567", created.GetProperty("phone").GetString());
        Assert.Equal(Email, created.GetProperty("email").GetString());
    }

    [SqlFact]
    public async Task Beneficiary_OneDayShortOfEighteenIsRejected()
    {
        // Chuẩn bị: ngày mai mới tròn 18 tuổi (thiếu đúng một ngày).
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        string oneDayShort = TodayInVietnam().AddYears(-18).AddDays(1).ToString("yyyy-MM-dd");

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = oneDayShort, email = Email });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("dateOfBirth", out _));
    }

    [SqlFact]
    public async Task Beneficiary_EditChangingBirthDateToUnderEighteenIsRejected()
    {
        // Chuẩn bị: hồ sơ người lớn hợp lệ.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement created = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        string url = $"{BeneficiariesUrl(vaultId)}/{created.GetProperty("id").GetGuid()}";
        string tenYearsAgo = TodayInVietnam().AddYears(-10).ToString("yyyy-MM-dd");

        // Hành động: đổi ngày sinh thành 10 tuổi.
        HttpResponseMessage put = await client.PutAsJsonAsync(url,
            new { etag = created.GetProperty("etag").GetString(), fullName = FullName, dateOfBirth = tenYearsAgo, email = Email });

        // Kiểm tra: bị từ chối ở trường dateOfBirth.
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(put, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("dateOfBirth", out _));
    }

    [SqlFact]
    public async Task Beneficiary_ResubmittingLegacyUnderEighteenProfileReturnsExistingNotError()
    {
        // Chuẩn bị: hồ sơ cũ chưa đủ 18 tuổi (tạo từ trước khi có quy tắc tuổi). Tạo người lớn rồi sửa thẳng ngày sinh trong DB.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement created = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        Guid createdId = created.GetProperty("id").GetGuid();
        string tenYearsAgo = TodayInVietnam().AddYears(-10).ToString("yyyy-MM-dd");
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            IBeneficiaryDataProtector protector = scope.ServiceProvider.GetRequiredService<IBeneficiaryDataProtector>();
            Beneficiary row = await db.Beneficiaries.SingleAsync(b => b.Id == createdId);
            BeneficiaryPersonalData data = protector.Unprotect(row);
            data.DateOfBirth = DateOnly.Parse(tenYearsAgo);
            protector.Protect(row, data);
            await db.SaveChangesAsync();
        }

        // Hành động: gửi lại đúng hồ sơ đó (cùng CCCD, họ tên, ngày sinh).
        HttpResponseMessage response = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = tenYearsAgo, email = Email });

        // Kiểm tra: 200 trả hồ sơ cũ, không tạo thêm. Chỉ hồ sơ mới mới bị kiểm tuổi.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement existing = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(createdId, existing.GetProperty("id").GetGuid());
        Assert.Equal(1, await CountBeneficiariesAsync(vaultId));
    }

    [SqlFact]
    public async Task Data02_FullCitizenIdIsReturnedAndIsStoredEncrypted()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);

        // Hành động
        HttpResponseMessage create = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(LeadingZeroCitizenId, FullName));
        string createBody = await create.Content.ReadAsStringAsync();
        Guid beneficiaryId = JsonDocument.Parse(createBody).RootElement.GetProperty("id").GetGuid();
        string getBody = await (await client.GetAsync($"{BeneficiariesUrl(vaultId)}/{beneficiaryId}")).Content.ReadAsStringAsync();
        string listBody = await (await client.GetAsync(BeneficiariesUrl(vaultId))).Content.ReadAsStringAsync();

        // Kiểm tra: response trả CCCD đầy đủ.
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Contains(LeadingZeroCitizenId, createBody);
        Assert.Contains(LeadingZeroCitizenId, getBody);
        Assert.Contains(LeadingZeroCitizenId, listBody);

        // Kiểm tra: DB không chứa CCCD, họ tên, email dạng UTF-8 rõ; DEK đã bọc 60 byte.
        Beneficiary? row = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            row = await db.Beneficiaries.AsNoTracking().FirstOrDefaultAsync(b => b.Id == beneficiaryId);
        });
        Assert.NotNull(row);
        Assert.Equal("8901", row.CitizenIdLast4);
        Assert.Equal(60, row.WrappedDataKey.Length);
        Assert.False(ContainsBytes(row.IdentityEncrypted, Encoding.UTF8.GetBytes(LeadingZeroCitizenId)));
        Assert.False(ContainsBytes(row.IdentityEncrypted, Encoding.UTF8.GetBytes(FullName)));
        Assert.False(ContainsBytes(row.ContactEncrypted, Encoding.UTF8.GetBytes(Email)));
    }

    [SqlFact]
    public async Task Quota03_CreatingBeneficiaryDoesNotBumpPlanConfigVersion()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        int versionBefore = await ApiTestHelpers.GetPlanConfigVersionAsync(client);

        // Hành động
        await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);

        // Kiểm tra
        Assert.Equal(versionBefore, await ApiTestHelpers.GetPlanConfigVersionAsync(client));
    }

    [SqlFact]
    public async Task Sec04_BeneficiaryOfAnotherVaultIsNotFound()
    {
        // Chuẩn bị: hồ sơ thuộc kho của owner; stranger dùng id đó trên kho của chính mình.
        (HttpClient owner, Guid ownerVaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement created = await ApiTestHelpers.CreateBeneficiaryAsync(owner, ownerVaultId, LeadingZeroCitizenId, FullName);
        (HttpClient stranger, Guid strangerVaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        string strangerUrl =$"{BeneficiariesUrl(strangerVaultId)}/{created.GetProperty("id").GetGuid()}";

        // Hành động
        HttpResponseMessage get = await stranger.GetAsync(strangerUrl);
        HttpResponseMessage put = await stranger.PutAsJsonAsync(strangerUrl, new { etag = "\"0\"", fullName = "Tên khác" });
        HttpResponseMessage delete = await stranger.DeleteAsync(strangerUrl);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(get, HttpStatusCode.NotFound, "BENEFICIARY_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(put, HttpStatusCode.NotFound, "BENEFICIARY_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(delete, HttpStatusCode.NotFound, "BENEFICIARY_NOT_FOUND");
        Assert.Equal(1, await CountBeneficiariesAsync(ownerVaultId));
    }

    [SqlFact]
    public async Task Edit_StaleEtagIsRejected()
    {
        // Chuẩn bị: sửa một lần để etag "0" thành cũ.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement created = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        string url = $"{BeneficiariesUrl(vaultId)}/{created.GetProperty("id").GetGuid()}";
        HttpResponseMessage firstEdit = await client.PutAsJsonAsync(url, new { etag = created.GetProperty("etag").GetString(), phone = "0901234567" });
        Assert.Equal(HttpStatusCode.OK, firstEdit.StatusCode);

        // Hành động
        HttpResponseMessage stale = await client.PutAsJsonAsync(url, new { etag = created.GetProperty("etag").GetString(), fullName = "Tên khác" });

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(stale, HttpStatusCode.Conflict, "EDIT_CONFLICT");
    }

    [SqlFact]
    public async Task Message_IsStoredReadBackKeptOnOtherEditsAndClearedByEmptyText()
    {
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        HttpResponseMessage create = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = "1990-05-20", email = Email, message = "  Con ơi,\nbố để lại phần này cho con.  " });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        JsonElement created = await ApiTestHelpers.ReadJsonAsync(create);
        Assert.Equal("Con ơi, bố để lại phần này cho con.", created.GetProperty("message").GetString());
        string url = $"{BeneficiariesUrl(vaultId)}/{created.GetProperty("id").GetGuid()}";

        // Sửa trường khác không đụng tới lời nhắn
        HttpResponseMessage rename = await client.PutAsJsonAsync(url,
            new { etag = created.GetProperty("etag").GetString(), fullName = "Nguyễn Văn Bình" });
        JsonElement renamed = await ApiTestHelpers.ReadJsonAsync(rename);
        Assert.Equal("Con ơi, bố để lại phần này cho con.", renamed.GetProperty("message").GetString());

        // Chuỗi rỗng thì xóa lời nhắn
        HttpResponseMessage clear = await client.PutAsJsonAsync(url,
            new { etag = renamed.GetProperty("etag").GetString(), message = "" });
        JsonElement cleared = await ApiTestHelpers.ReadJsonAsync(clear);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("message").ValueKind);
    }

    [SqlFact]
    public async Task Message_LongerThan300CharactersIsRejected()
    {
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);

        HttpResponseMessage response = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = "1990-05-20", email = Email, message = new string('a', 301) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountBeneficiariesAsync(vaultId));
    }

    [SqlFact]
    public async Task Edit_RenameChangesEtagAndIsReadBack()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement created = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        string url = $"{BeneficiariesUrl(vaultId)}/{created.GetProperty("id").GetGuid()}";

        // Hành động
        HttpResponseMessage put = await client.PutAsJsonAsync(url,
            new { etag = created.GetProperty("etag").GetString(), fullName = "  Nguyễn   Văn Bình " });
        HttpResponseMessage get = await client.GetAsync(url);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        JsonElement updated = await ApiTestHelpers.ReadJsonAsync(put);
        Assert.NotEqual(created.GetProperty("etag").GetString(), updated.GetProperty("etag").GetString());
        Assert.Equal("Nguyễn Văn Bình", updated.GetProperty("fullName").GetString());
        JsonElement readBack = await ApiTestHelpers.ReadJsonAsync(get);
        Assert.Equal("Nguyễn Văn Bình", readBack.GetProperty("fullName").GetString());
        Assert.Equal(LeadingZeroCitizenId, readBack.GetProperty("citizenId").GetString());
        Assert.Equal(Email, readBack.GetProperty("email").GetString());
        Assert.Equal(updated.GetProperty("etag").GetString(), readBack.GetProperty("etag").GetString());
    }

    [SqlFact]
    public async Task Edit_NothingChangedKeepsEtag()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement created = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        string url = $"{BeneficiariesUrl(vaultId)}/{created.GetProperty("id").GetGuid()}";

        // Hành động: gửi lại đúng giá trị đang có (email khác hoa thường vẫn là cùng email sau chuẩn hóa).
        HttpResponseMessage put = await client.PutAsJsonAsync(url,
            new { etag = created.GetProperty("etag").GetString(), fullName = FullName, dateOfBirth = "1990-05-20", email = " NguoiNhan@Example.com " });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        JsonElement same = await ApiTestHelpers.ReadJsonAsync(put);
        Assert.Equal("\"0\"", same.GetProperty("etag").GetString());
        Assert.Equal(JsonValueKind.Null, same.GetProperty("updatedAt").ValueKind);
    }

    [SqlFact]
    public async Task Section52Step7_EditRemovingBothEmailAndPhoneIsRejected()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement created = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        string url = $"{BeneficiariesUrl(vaultId)}/{created.GetProperty("id").GetGuid()}";

        // Hành động
        HttpResponseMessage put = await client.PutAsJsonAsync(url,
            new { etag = created.GetProperty("etag").GetString(), email = "", phone = "" });

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(put, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task StaffAccountEmailCannotBeUsedAsRecipient_OnCreateOrUpdate()
    {
        // Chuẩn bị: email của tài khoản nhân sự (Executor, Verifier, Admin) và một người nhận hợp lệ
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        string staffMessage = "Email này thuộc tài khoản nhân sự của LegacyVault nên không thể làm người nhận.";
        UserRole[] staffRoles = new UserRole[] { UserRole.Executor, UserRole.Verifier, UserRole.Admin };
        foreach (UserRole role in staffRoles)
        {
            User staff = await VaultTestData.AddUserAsync(factory, role, UserStatus.Active);

            // Hành động: tạo người nhận bằng email nhân sự
            HttpResponseMessage create = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
                new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = "1990-05-20", email = staff.Email.ToUpperInvariant() });

            // Kiểm tra: 400 ở trường email
            JsonElement problem = await ApiTestHelpers.AssertProblemAsync(create, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            JsonElement emailErrors = problem.GetProperty("errors").GetProperty("email");
            Assert.Equal(staffMessage, emailErrors[0].GetString());
        }
        Assert.Equal(0, await CountBeneficiariesAsync(vaultId));

        // Sửa người nhận hợp lệ sang email nhân sự cũng bị chặn, dữ liệu giữ nguyên
        JsonElement created = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpResponseMessage update = await client.PutAsJsonAsync($"{BeneficiariesUrl(vaultId)}/{created.GetProperty("id").GetGuid()}",
            new { etag = created.GetProperty("etag").GetString(), email = admin.Email });
        await ApiTestHelpers.AssertProblemAsync(update, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        JsonElement stored = await ApiTestHelpers.ReadJsonAsync(
            await client.GetAsync($"{BeneficiariesUrl(vaultId)}/{created.GetProperty("id").GetGuid()}"));
        Assert.Equal(Email, stored.GetProperty("email").GetString());
    }

    [SqlFact]
    public async Task RecipientEmailCannotRepeatAnotherRecipientInTheSameVault_OnCreateOrUpdate()
    {
        // Chuẩn bị: một người nhận đã có email chung
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        string message = "Email này đã dùng cho một người nhận khác trong kho.";

        // Hành động: người thứ hai khác CCCD nhưng dùng lại email đó
        HttpResponseMessage create = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = SameLast4CitizenId, fullName = "Trần Thị Bình", dateOfBirth = "1990-05-20", email = " " + Email.ToUpperInvariant() + " " });

        // Kiểm tra: 400 ở trường email, chưa tạo thêm hồ sơ
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(create, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(message, problem.GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.Equal(1, await CountBeneficiariesAsync(vaultId));

        // Sửa người thứ hai sang email của người thứ nhất cũng bị chặn; gửi lại đúng người thứ nhất (cùng email) thì không
        JsonElement second = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, SameLast4CitizenId, "Trần Thị Bình");
        HttpResponseMessage update = await client.PutAsJsonAsync($"{BeneficiariesUrl(vaultId)}/{second.GetProperty("id").GetGuid()}",
            new { etag = second.GetProperty("etag").GetString(), email = Email });
        await ApiTestHelpers.AssertProblemAsync(update, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        HttpResponseMessage resend = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(LeadingZeroCitizenId, FullName));
        Assert.True(resend.IsSuccessStatusCode);
    }

    [SqlFact]
    public async Task OrdinaryAccountEmailIsStillAllowedAsRecipient()
    {
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        User ordinary = await VaultTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);

        HttpResponseMessage create = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = "1990-05-20", email = ordinary.Email });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    [SqlFact]
    public async Task Data02_SearchOnlyByLast4Digits()
    {
        // Chuẩn bị: hai hồ sơ khác 4 số cuối.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement matching = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, "079123451234", "Trần Thị Bình");

        // Hành động
        HttpResponseMessage search = await client.GetAsync($"{BeneficiariesUrl(vaultId)}?searchTerm=8901");
        HttpResponseMessage byName = await client.GetAsync($"{BeneficiariesUrl(vaultId)}?searchTerm=An");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(search);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        JsonElement items = page.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(matching.GetProperty("id").GetGuid(), items[0].GetProperty("id").GetGuid());
        await ApiTestHelpers.AssertProblemAsync(byName, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task Oplan01_GraceReadOnlyBlocksCreateButAllowsDelete()
    {
        // Chuẩn bị: có sẵn một hồ sơ, rồi chuyển dịch vụ sang ân hạn chỉ đọc.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement existing = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        await ApiTestHelpers.SetEntitlementStatusAsync(factory, vaultId, EntitlementStatus.GraceReadOnly);

        // Hành động
        HttpResponseMessage create = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(SameLast4CitizenId, "Trần Thị Bình"));
        HttpResponseMessage delete = await client.DeleteAsync($"{BeneficiariesUrl(vaultId)}/{existing.GetProperty("id").GetGuid()}");

        // Kiểm tra: tạo là thao tác "thêm" → chặn; xóa là thao tác "giảm" → cho.
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(create, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
        Assert.Equal("SERVICE_READ_ONLY", problem.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(0, await CountBeneficiariesAsync(vaultId));
    }

    [SqlFact]
    public async Task Delete_ThenGetReturnsNotFound()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement created = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        string url = $"{BeneficiariesUrl(vaultId)}/{created.GetProperty("id").GetGuid()}";

        // Hành động
        HttpResponseMessage delete = await client.DeleteAsync(url);
        HttpResponseMessage get = await client.GetAsync(url);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        await ApiTestHelpers.AssertProblemAsync(get, HttpStatusCode.NotFound, "BENEFICIARY_NOT_FOUND");
    }

    [SqlFact]
    public async Task ProfileLimit_ResendingExistingPersonAtLimitReturnsExisting()
    {
        // Chuẩn bị: kho đã đủ 2 hồ sơ.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement first = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, SameLast4CitizenId, FullName);

        // Hành động: gửi lại đúng người đã có (cùng CCCD, họ tên, ngày sinh).
        HttpResponseMessage resend = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(LeadingZeroCitizenId, FullName));

        // Kiểm tra: vẫn trả hồ sơ cũ, không bị chặn vì suất.
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        JsonElement existing = await ApiTestHelpers.ReadJsonAsync(resend);
        Assert.Equal(first.GetProperty("id").GetGuid(), existing.GetProperty("id").GetGuid());
        Assert.Equal(2, await CountBeneficiariesAsync(vaultId));
    }

    [SqlFact]
    public async Task ProfileLimit_DeletingUnusedProfileFreesRoom()
    {
        // Chuẩn bị: kho đã đủ 2 hồ sơ.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement first = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, SameLast4CitizenId, FullName);

        // Hành động: xóa một hồ sơ chưa ở gói nào rồi tạo người mới.
        HttpResponseMessage delete = await client.DeleteAsync($"{BeneficiariesUrl(vaultId)}/{first.GetProperty("id").GetGuid()}");
        HttpResponseMessage create = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(ThirdCitizenId, FullName));

        // Kiểm tra
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(2, await CountBeneficiariesAsync(vaultId));
    }

    private static string BeneficiariesUrl(Guid vaultId)
    {
        return $"/api/v1/vaults/{vaultId}/beneficiaries";
    }

    private static object AdultBody(string citizenId, string fullName)
    {
        // Mỗi người một email riêng (email không được trùng người nhận khác trong kho); người chính giữ email chung.
        string email = citizenId == LeadingZeroCitizenId ? Email : $"nn{citizenId.Replace(" ", "")}@example.com";
        return new { citizenId, fullName, dateOfBirth = "1990-05-20", email };
    }

    /// <summary>Số dòng Beneficiaries của kho (đọc thẳng DB).</summary>
    private async Task<int> CountBeneficiariesAsync(Guid vaultId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.Beneficiaries.CountAsync(b => b.VaultId == vaultId);
        });
        return count;
    }

    private static DateOnly TodayInVietnam()
    {
        return DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        return haystack.AsSpan().IndexOf(needle) >= 0;
    }
}
