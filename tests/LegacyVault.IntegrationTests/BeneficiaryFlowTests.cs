using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
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
        Assert.Equal("8901", created.GetProperty("citizenIdLast4").GetString());
        Assert.Equal(FullName, created.GetProperty("fullName").GetString());
        Assert.Equal("1990-05-20", created.GetProperty("dateOfBirth").GetString());
        Assert.Equal(Email, created.GetProperty("email").GetString());
        Assert.Equal(0, created.GetProperty("containerIds").GetArrayLength());
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
    public async Task Beneficiary_MinorIsSavedWithoutConfirmation()
    {
        // Chuẩn bị: người nhận 10 tuổi. Đã bỏ SETUP-19 nên không cần xác nhận gì.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        string tenYearsAgo = TodayInVietnam().AddYears(-10).ToString("yyyy-MM-dd");

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = tenYearsAgo, email = Email, phone = "0901 234.567" });

        // Kiểm tra: lưu được ngay; không còn nhãn isMinor, minorAcknowledgedAt luôn null với hồ sơ mới.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement created = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.False(created.TryGetProperty("isMinor", out _));
        Assert.Equal(JsonValueKind.Null, created.GetProperty("minorAcknowledgedAt").ValueKind);
        Assert.Equal("0901234567", created.GetProperty("phone").GetString());
        Assert.Equal(Email, created.GetProperty("email").GetString());
    }

    [SqlFact]
    public async Task Beneficiary_LegacyConfirmMinorFieldIsIgnored()
    {
        // Chuẩn bị: FE cũ còn gửi confirmMinor = true dù API không còn trường này.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        string tenYearsAgo = TodayInVietnam().AddYears(-10).ToString("yyyy-MM-dd");

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId),
            new { citizenId = LeadingZeroCitizenId, fullName = FullName, dateOfBirth = tenYearsAgo, email = Email, phone = "0901 234.567", confirmMinor = true });

        // Kiểm tra: trường thừa bị bỏ qua, không lỗi và không ghi dấu xác nhận.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement created = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("minorAcknowledgedAt").ValueKind);
    }

    [SqlFact]
    public async Task Data02_FullCitizenIdIsNeverReturnedAndIsStoredEncrypted()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);

        // Hành động
        HttpResponseMessage create = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(LeadingZeroCitizenId, FullName));
        string createBody = await create.Content.ReadAsStringAsync();
        Guid beneficiaryId = JsonDocument.Parse(createBody).RootElement.GetProperty("id").GetGuid();
        string getBody = await (await client.GetAsync($"{BeneficiariesUrl(vaultId)}/{beneficiaryId}")).Content.ReadAsStringAsync();
        string listBody = await (await client.GetAsync(BeneficiariesUrl(vaultId))).Content.ReadAsStringAsync();

        // Kiểm tra: response không có CCCD đầy đủ.
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.DoesNotContain(LeadingZeroCitizenId, createBody);
        Assert.DoesNotContain(LeadingZeroCitizenId, getBody);
        Assert.DoesNotContain(LeadingZeroCitizenId, listBody);

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
        Assert.Equal("8901", readBack.GetProperty("citizenIdLast4").GetString());
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
    public async Task ProfileLimit_CreatingBeyondSlotsIsRejected_ExtraSlotAllowsIt()
    {
        // Chuẩn bị: XS có 2 suất, kho đã có 2 hồ sơ chưa ở gói nào.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, LeadingZeroCitizenId, FullName);
        await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, SameLast4CitizenId, FullName);

        // Hành động: tạo hồ sơ thứ 3 khi đã đủ suất.
        HttpResponseMessage overLimit = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(ThirdCitizenId, FullName));

        // Kiểm tra: bị chặn, không thêm dòng nào.
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(overLimit, HttpStatusCode.Conflict, "BENEFICIARY_SLOTS_EXCEEDED");
        Assert.Equal(2, problem.GetProperty("usedSlots").GetInt32());
        Assert.Equal(2, problem.GetProperty("maxSlots").GetInt32());
        Assert.Equal(2, await CountBeneficiariesAsync(vaultId));

        // Hành động: mua thêm 1 suất rồi tạo lại.
        await ApiTestHelpers.SetExtraBeneficiarySlotsAsync(factory, vaultId, 1);
        HttpResponseMessage withExtraSlot = await client.PostAsJsonAsync(BeneficiariesUrl(vaultId), AdultBody(ThirdCitizenId, FullName));

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Created, withExtraSlot.StatusCode);
        Assert.Equal(3, await CountBeneficiariesAsync(vaultId));
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
        return new { citizenId, fullName, dateOfBirth = "1990-05-20", email = Email };
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
