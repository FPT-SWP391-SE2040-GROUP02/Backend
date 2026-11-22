using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.Audit;
using LegacyVault.IntegrationTests.TestSupport;
using LegacyVault.IntegrationTests.Ekyc;
using LegacyVault.IntegrationTests.GoogleAuth;
using LegacyVault.Infrastructure.Reference;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Kho nội dung giả có lưu thật vào bộ nhớ (FakeAssetContentStore dùng chung không hỗ trợ SaveAsync).</summary>
public sealed class InMemoryContentStore : IAssetContentStore
{
    public Dictionary<string, byte[]> Contents { get; } = new Dictionary<string, byte[]>();

    public async Task<StoredContent> SaveAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        using (MemoryStream buffer = new MemoryStream())
        {
            await content.CopyToAsync(buffer, ct);
            string key = "img-" + Guid.NewGuid().ToString("N");
            Contents[key] = buffer.ToArray();
            return new StoredContent
            {
                StorageKey = key,
                SizeBytes = buffer.Length,
                EncryptionKeyId = "k1",
                WrappedDataKey = new byte[] { 1, 2, 3 },
            };
        }
    }

    public async Task CopyToAsync(StoredContent content, Stream destination, CancellationToken ct)
    {
        await destination.WriteAsync(Contents[content.StorageKey], ct);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        Contents.Remove(storageKey);
        return Task.CompletedTask;
    }
}

public sealed class FakeHandoverSubmissionRepository : IHandoverSubmissionRepository
{
    public List<HandoverSubmission> Items { get; } = new List<HandoverSubmission>();

    /// <summary>Cặp (Người xét duyệt, kho) mà Người xét duyệt đó đã duyệt hồ sơ chứng tử.</summary>
    public HashSet<(Guid Verifier, Guid Vault)> Approvers { get; } = new HashSet<(Guid, Guid)>();

    public void Add(HandoverSubmission submission)
    {
        Items.Add(submission);
    }

    public Task<HandoverSubmission?> FindLatestByTurnAsync(Guid turnId, CancellationToken ct)
    {
        return Task.FromResult(Items.Where(s => s.TurnId == turnId).OrderByDescending(s => s.CreatedAt).FirstOrDefault());
    }

    public Task<HandoverSubmission?> FindByIdWithTurnAsync(Guid submissionId, CancellationToken ct)
    {
        return Task.FromResult(Items.FirstOrDefault(s => s.Id == submissionId));
    }

    public Task<List<HandoverSubmission>> ListPendingForVerifierAsync(Guid verifierUserId, CancellationToken ct)
    {
        return Task.FromResult(Items
            .Where(s => s.Status == HandoverSubmissionStatus.Submitted && Approvers.Contains((verifierUserId, s.Turn.VaultId)))
            .OrderBy(s => s.CreatedAt).ToList());
    }

    public Task<List<HandoverSubmission>> ListByTurnIdsAsync(List<Guid> turnIds, CancellationToken ct)
    {
        return Task.FromResult(Items.Where(s => turnIds.Contains(s.TurnId)).ToList());
    }

    /// <summary>Người xét duyệt (họ tên, số thẻ) theo kho, đặt sẵn trong test.</summary>
    public Dictionary<Guid, (string FullName, string? Card)> Reviewers { get; } = new Dictionary<Guid, (string, string?)>();

    public Task<ApprovingVerifierRow?> FindApprovingVerifierAsync(Guid vaultId, CancellationToken ct)
    {
        if (Reviewers.TryGetValue(vaultId, out var row))
        {
            ApprovingVerifierRow found = new ApprovingVerifierRow { FullName = row.FullName, LawyerCardNumber = row.Card };
            return Task.FromResult<ApprovingVerifierRow?>(found);
        }
        return Task.FromResult<ApprovingVerifierRow?>(null);
    }

    public Task<Guid?> FindApprovingVerifierUserIdAsync(Guid vaultId, CancellationToken ct)
    {
        foreach ((Guid verifierId, Guid approvedVaultId) in Approvers)
        {
            if (approvedVaultId == vaultId)
            {
                return Task.FromResult<Guid?>(verifierId);
            }
        }
        return Task.FromResult<Guid?>(null);
    }

    public Task<bool> IsApprovingVerifierAsync(Guid verifierUserId, Guid vaultId, CancellationToken ct)
    {
        return Task.FromResult(Approvers.Contains((verifierUserId, vaultId)));
    }
}

/// <summary>Hồ sơ người nhận: nộp (ảnh căn cước, bản khai đối chiếu với thông tin Chủ kho) và Người xét duyệt duyệt.</summary>
public sealed class HandoverSubmissionTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
    private static readonly byte[] Jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1 };

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository handover = new FakeHandoverRepository();
    private readonly FakeHandoverSubmissionRepository submissions = new FakeHandoverSubmissionRepository();
    private readonly FakeEkycBeneficiaryRepository beneficiaries = new FakeEkycBeneficiaryRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
    private readonly InMemoryContentStore store = new InMemoryContentStore();
    private readonly FakeAuditLogRepository audit = new FakeAuditLogRepository();
    private readonly FakeHoldReader holds = new FakeHoldReader();
    private readonly FakeHandoverRecordRepository records = new FakeHandoverRecordRepository();
    private readonly CountingRecordService recordService = new CountingRecordService();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider time = new FixedTimeProvider(Now);
    private readonly User recipient;
    private readonly User verifier;
    private readonly HandoverTurn turn;
    private readonly HandoverSubmissionService submitService;
    private readonly HandoverSubmissionReviewService reviewService;

    public HandoverSubmissionTests()
    {
        recipient = new User { Email = "nguoi-nhan@example.com", FullName = "Trần Minh Anh", Status = UserStatus.Active, EmailVerifiedAt = Now.AddDays(-10) };
        verifier = new User { Email = "ls@example.com", FullName = "Luật sư Dũng", Role = UserRole.Verifier, Status = UserStatus.Active, EmailVerifiedAt = Now.AddDays(-10) };
        users.Users.Add(recipient);
        users.Users.Add(verifier);
        users.Users.Add(data.Owner);
        vaults.Vaults.Add(data.Vault);

        Container container = data.AddContainer("Gói gia đình", ContainerStatus.Ready, 2);
        Designation designation = data.Designate(container, "Trần Minh Anh", "minh-anh@example.com");
        beneficiaries.Beneficiaries.Add(designation.Beneficiary);
        protector.Data[designation.BeneficiaryId] = data.People[designation.BeneficiaryId];

        turn = data.BuildTurn(container, designation, HandoverTurnStatus.IdentityInProgress, "token-" + Guid.NewGuid().ToString("N"), recipient.Id);
        // Biên bản đã chốt, đang ở bước kiểm danh tính: luật sư mới xác nhận được.
        HandoverRecord record = new HandoverRecord();
        record.VaultId = data.Vault.Id;
        record.PlanId = data.Plan.Id;
        record.VerifierUserId = verifier.Id;
        record.Status = HandoverRecordStatus.IdentityCheck;
        records.Records.Add(record);
        turn.RecordId = record.Id;
        handover.Turns.Add(turn);
        submissions.Approvers.Add((verifier.Id, data.Vault.Id));
        currentUser.UserId = recipient.Id;

        submitService = new HandoverSubmissionService(
            currentUser, handover, submissions, beneficiaries, protector, store, unitOfWork, time);
        reviewService = new HandoverSubmissionReviewService(
            currentUser, users, vaults, submissions, holds, store, audit, unitOfWork, time, records, recordService);
    }

    private static SubmitHandoverSubmissionRequest Request(
        string kind = "ACCEPT", string name = "Trần Minh Anh", string citizenId = "012345671234",
        DateOnly? dob = null, bool acknowledged = true, bool voluntary = false)
    {
        return new SubmitHandoverSubmissionRequest
        {
            Kind = kind,
            FullName = name,
            CitizenId = citizenId,
            DateOfBirth = dob ?? new DateOnly(1990, 1, 1),
            ResidenceAddress = "34 Trần Hưng Đạo, Hà Nội",
            Acknowledged = acknowledged,
            VoluntaryDecline = voluntary,
        };
    }

    private Task<HandoverSubmissionResponse> Submit(SubmitHandoverSubmissionRequest request)
    {
        return submitService.SubmitAsync(turn.Id, request, Png, Jpeg, CancellationToken.None);
    }

    private HandoverSubmission Stored()
    {
        return submissions.Items.Last();
    }

    // Gắn Turn cho bản ghi vì fake repository không có EF để tự nạp.
    private async Task<HandoverSubmission> SubmitAndAttach(SubmitHandoverSubmissionRequest request)
    {
        await Submit(request);
        HandoverSubmission submission = Stored();
        submission.Turn = turn;
        return submission;
    }

    // ---- Người nhận nộp hồ sơ ----

    [Fact]
    public async Task Submit_Accept_KeepsMatchFlagsCitizenIdAndEncryptedImages()
    {
        HandoverSubmissionResponse response = await Submit(Request());

        HandoverSubmission saved = Stored();
        Assert.Equal("ACCEPT", response.Kind);
        Assert.Equal("SUBMITTED", response.Status);
        Assert.True(saved.NameMatches && saved.CitizenIdMatches && saved.DateOfBirthMatches);
        Assert.Equal("012345671234", saved.CitizenId);
        Assert.Equal(2, store.Contents.Count);
        Assert.Equal("image/png", saved.FrontImageContentType);
        Assert.Equal("image/jpeg", saved.BackImageContentType);
    }

    [Fact]
    public async Task Submit_WrongNameOrIdOrBirth_SetsOnlyThoseFlagsFalse()
    {
        await Submit(Request(name: "Người Khác", citizenId: "999999999999", dob: new DateOnly(1991, 2, 2)));

        HandoverSubmission saved = Stored();
        Assert.False(saved.NameMatches);
        Assert.False(saved.CitizenIdMatches);
        Assert.False(saved.DateOfBirthMatches);
    }

    [Fact]
    public async Task Submit_NameComparisonIgnoresCaseAndAccents()
    {
        await Submit(Request(name: "TRAN MINH ANH"));

        Assert.True(Stored().NameMatches);
    }

    [Fact]
    public async Task Submit_WithoutAcknowledging_IsRejected()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => Submit(Request(acknowledged: false)));

        Assert.Equal(AppErrorType.Validation, error.Type);
        Assert.Empty(submissions.Items);
        Assert.Empty(store.Contents);
    }

    [Fact]
    public async Task Submit_NotImageBytes_IsRejectedAndStoresNothing()
    {
        byte[] text = System.Text.Encoding.UTF8.GetBytes("khong phai anh");

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => submitService.SubmitAsync(turn.Id, Request(), text, Jpeg, CancellationToken.None));

        Assert.Equal(AppErrorType.Validation, error.Type);
        Assert.Empty(store.Contents);
    }

    [Fact]
    public async Task Submit_TurnOfAnotherUser_IsNotFound()
    {
        currentUser.UserId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(() => Submit(Request()));

        Assert.Equal(AppErrorType.NotFound, error.Type);
        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
    }

    [Fact]
    public async Task Submit_WhenTurnIsNotInIdentityStep_IsConflict()
    {
        turn.Status = HandoverTurnStatus.ReadyToAccept;

        AppException error = await Assert.ThrowsAsync<AppException>(() => Submit(Request()));

        Assert.Equal(AppErrorType.Conflict, error.Type);
    }

    [Fact]
    public async Task Resubmit_SupersedesOldOneAndDeletesItsImages()
    {
        await Submit(Request());
        HandoverSubmission first = Stored();
        string oldFront = first.FrontImageStorageKey;

        time.Now = Now.AddMinutes(5);
        await Submit(Request(name: "Trần Minh Anh"));

        Assert.Equal(HandoverSubmissionStatus.Superseded, first.Status);
        Assert.False(store.Contents.ContainsKey(oldFront));
        Assert.Equal(2, store.Contents.Count);
        HandoverSubmissionResponse? latest = await submitService.GetLatestAsync(turn.Id, CancellationToken.None);
        Assert.Equal("SUBMITTED", latest!.Status);
    }

    [Fact]
    public async Task Submit_AfterApproval_IsConflict()
    {
        HandoverSubmission submission = await SubmitAndAttach(Request());
        currentUser.UserId = verifier.Id;
        await reviewService.ApproveAsync(submission.Id, CancellationToken.None);
        currentUser.UserId = recipient.Id;
        turn.Status = HandoverTurnStatus.IdentityInProgress;

        AppException error = await Assert.ThrowsAsync<AppException>(() => Submit(Request()));

        Assert.Equal(AppErrorType.Conflict, error.Type);
    }

    [Fact]
    public async Task GetLatest_BeforeAnySubmission_ReturnsNull()
    {
        Assert.Null(await submitService.GetLatestAsync(turn.Id, CancellationToken.None));
    }

    // ---- Người xét duyệt ----

    [Fact]
    public async Task List_ShowsPendingOnlyToTheVerifierWhoApprovedTheCase()
    {
        await SubmitAndAttach(Request());
        currentUser.UserId = verifier.Id;

        List<VerifierHandoverSubmissionSummaryResponse> mine = await reviewService.ListPendingAsync(CancellationToken.None);

        VerifierHandoverSubmissionSummaryResponse row = Assert.Single(mine);
        Assert.Equal("Gói gia đình", row.ContainerName);
        Assert.True(row.AllMatch);

        User other = new User { Email = "ls2@example.com", FullName = "Luật sư Khác", Role = UserRole.Verifier, Status = UserStatus.Active, EmailVerifiedAt = Now };
        users.Users.Add(other);
        currentUser.UserId = other.Id;
        Assert.Empty(await reviewService.ListPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Approve_WhenAllMatch_MovesTurnToReadyToAcceptAndAuditsIt()
    {
        HandoverSubmission submission = await SubmitAndAttach(Request());
        currentUser.UserId = verifier.Id;

        VerifierHandoverSubmissionResponse response = await reviewService.ApproveAsync(submission.Id, CancellationToken.None);

        Assert.Equal("APPROVED", response.Status);
        Assert.Equal(HandoverTurnStatus.ReadyToAccept, turn.Status);
        Assert.Equal(verifier.Id, turn.IdentityConfirmedByUserId);
        Assert.Null(turn.DecisionDeadline);
        Assert.Equal(1, recordService.AdvanceCalls);
        Assert.Equal(AuditAction.HandoverSubmissionApproved, Assert.Single(audit.Logs).Action);
    }

    [Fact]
    public async Task Approve_WhenRecordIsReopenedForEditing_IsConflict()
    {
        HandoverSubmission submission = await SubmitAndAttach(Request());
        currentUser.UserId = verifier.Id;
        records.Records[0].Status = HandoverRecordStatus.Drafting;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => reviewService.ApproveAsync(submission.Id, CancellationToken.None));

        Assert.Equal(AppErrorType.Conflict, error.Type);
        Assert.Equal("NOT_IDENTITY_STEP", error.Extensions["reason"]);
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    [Fact]
    public async Task Approve_Twice_ReturnsSameResultWithoutSecondAudit()
    {
        HandoverSubmission submission = await SubmitAndAttach(Request());
        currentUser.UserId = verifier.Id;
        await reviewService.ApproveAsync(submission.Id, CancellationToken.None);

        VerifierHandoverSubmissionResponse again = await reviewService.ApproveAsync(submission.Id, CancellationToken.None);

        Assert.Equal("APPROVED", again.Status);
        Assert.Single(audit.Logs);
    }

    [Fact]
    public async Task Approve_WhenAnyFieldMismatches_VerifierStillDecides()
    {
        HandoverSubmission submission = await SubmitAndAttach(Request(citizenId: "999999999999"));
        currentUser.UserId = verifier.Id;

        VerifierHandoverSubmissionResponse detail = await reviewService.GetAsync(submission.Id, CancellationToken.None);
        VerifierHandoverSubmissionResponse approved = await reviewService.ApproveAsync(submission.Id, CancellationToken.None);

        Assert.False(detail.AllMatch);
        Assert.True(detail.CanApprove);
        Assert.True(detail.CanRequestSupplement);
        Assert.Equal("APPROVED", approved.Status);
        Assert.NotEqual(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    [Fact]
    public async Task RequestSupplement_NeedsReason_SetsStatusAndLetsRecipientResubmit()
    {
        HandoverSubmission submission = await SubmitAndAttach(Request());
        currentUser.UserId = verifier.Id;

        AppException missing = await Assert.ThrowsAsync<AppException>(() => reviewService.RequestSupplementAsync(
            submission.Id, new RequestHandoverSupplementRequest { Reason = "   " }, CancellationToken.None));
        VerifierHandoverSubmissionResponse response = await reviewService.RequestSupplementAsync(
            submission.Id, new RequestHandoverSupplementRequest { Reason = "Ảnh mặt sau bị mờ" }, CancellationToken.None);

        Assert.Equal(AppErrorType.Validation, missing.Type);
        Assert.Equal("SUPPLEMENT_REQUESTED", response.Status);
        Assert.Equal("Ảnh mặt sau bị mờ", response.SupplementReason);
        Assert.Equal(AuditAction.HandoverSubmissionSupplementRequested, Assert.Single(audit.Logs).Action);

        currentUser.UserId = recipient.Id;
        time.Now = Now.AddHours(1);
        HandoverSubmissionResponse again = await Submit(Request());
        Assert.Equal("SUBMITTED", again.Status);
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    [Fact]
    public async Task Review_ByNonVerifier_IsForbidden()
    {
        HandoverSubmission submission = await SubmitAndAttach(Request());
        currentUser.UserId = recipient.Id;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => reviewService.ApproveAsync(submission.Id, CancellationToken.None));

        Assert.Equal(AppErrorType.Forbidden, error.Type);
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    [Fact]
    public async Task Review_ByVerifierWhoDidNotApproveTheCase_IsNotFound()
    {
        HandoverSubmission submission = await SubmitAndAttach(Request());
        User other = new User { Email = "ls3@example.com", FullName = "Luật sư Ba", Role = UserRole.Verifier, Status = UserStatus.Active, EmailVerifiedAt = Now };
        users.Users.Add(other);
        currentUser.UserId = other.Id;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => reviewService.GetAsync(submission.Id, CancellationToken.None));

        Assert.Equal(AppErrorType.NotFound, error.Type);
    }

    [Fact]
    public async Task Approve_WhenKhoIsHeld_IsConflict()
    {
        HandoverSubmission submission = await SubmitAndAttach(Request());
        currentUser.UserId = verifier.Id;
        holds.Active = true;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => reviewService.ApproveAsync(submission.Id, CancellationToken.None));

        Assert.Equal(AppErrorType.Conflict, error.Type);
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    [Fact]
    public async Task Image_ReturnsTheStoredBytesToTheAssignedVerifier()
    {
        HandoverSubmission submission = await SubmitAndAttach(Request());
        currentUser.UserId = verifier.Id;

        PreparedDownload download = await reviewService.PrepareImageAsync(submission.Id, "front", CancellationToken.None);
        using (MemoryStream destination = new MemoryStream())
        {
            await download.WriteToAsync(destination, CancellationToken.None);
            Assert.Equal(Png, destination.ToArray());
        }
        Assert.Equal("image/png", download.ContentType);

        AppException bad = await Assert.ThrowsAsync<AppException>(
            () => reviewService.PrepareImageAsync(submission.Id, "side", CancellationToken.None));
        Assert.Equal(AppErrorType.NotFound, bad.Type);
    }
}

/// <summary>Biên bản giả: chỉ đếm số lần được báo đã xác nhận danh tính.</summary>
public sealed class CountingRecordService : IHandoverRecordService
{
    public int AdvanceCalls { get; private set; }

    public Task AdvanceAfterIdentityAsync(HandoverTurn turn, DateTimeOffset now, CancellationToken ct)
    {
        AdvanceCalls++;
        turn.DecisionDeadline = null;
        return Task.CompletedTask;
    }

    public Task<Guid?> FindVerifierUserIdAsync(Guid vaultId, CancellationToken ct) => throw new NotSupportedException();
    public Task<List<ExecutorVaultResponse>> ListMyVaultsAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse?> GetForExecutorAsync(Guid vaultId, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> SignAsExecutorAsync(Guid vaultId, CancellationToken ct) => throw new NotSupportedException();
    public Task<List<HandoverRecordSummaryResponse>> ListForVerifierAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> GetForVerifierAsync(Guid recordId, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> UseOwnerSplitAsync(Guid recordId, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> SetLinesAsync(Guid recordId, SetHandoverRecordLinesRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> FinalizeAsync(Guid recordId, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> ReopenAsync(Guid recordId, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> SignAsVerifierAsync(Guid recordId, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> GetForRecipientAsync(Guid turnId, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> SignAsRecipientAsync(Guid turnId, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> ReviewClaimAsync(Guid recordId, Guid claimId, ReviewHandoverClaimRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> AddRecipientAsync(Guid recordId, AddHandoverRecipientRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> AddEvidenceAsync(Guid recordId, List<UploadedFile> files, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRecordResponse> RemoveEvidenceAsync(Guid recordId, Guid fileId, CancellationToken ct) => throw new NotSupportedException();
    public Task<PreparedDownload> PrepareVerifierFileAsync(Guid recordId, Guid fileId, CancellationToken ct) => throw new NotSupportedException();
    public Task<HandoverRequestPreviewResponse> PreviewRequestAsync(string token, CancellationToken ct) => throw new NotSupportedException();
    public Task<MyHandoverClaimResponse> SubmitClaimAsync(string token, string? note, List<UploadedFile> files, CancellationToken ct) => throw new NotSupportedException();
    public Task<List<MyHandoverClaimResponse>> ListMyClaimsAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<MyHandoverClaimResponse> SupplementClaimAsync(Guid claimId, string? note, List<UploadedFile> files, CancellationToken ct) => throw new NotSupportedException();
    public Task<PreparedDownload> PrepareMyClaimFileAsync(Guid claimId, Guid fileId, CancellationToken ct) => throw new NotSupportedException();
    public Task<OwnerHandoverRequestResponse?> GetOwnerRequestAsync(Guid vaultId, CancellationToken ct) => throw new NotSupportedException();
    public Task CancelByOwnerAsync(Guid vaultId, CancellationToken ct) => throw new NotSupportedException();
}
