using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.Audit;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.Ekyc;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Kho biên bản giả: lưu trong List. VerifierVaults là các kho mà từng Người xét duyệt đã duyệt hồ sơ chứng tử.</summary>
public sealed class FakeHandoverDocumentRepository : IHandoverDocumentRepository
{
    public List<HandoverDocument> Items { get; } = new List<HandoverDocument>();

    public List<(Guid VerifierId, Guid VaultId)> VerifierVaults { get; } = new List<(Guid, Guid)>();

    public void Add(HandoverDocument document)
    {
        Items.Add(document);
    }

    public Task<HandoverDocument?> FindByIdAsync(Guid documentId, CancellationToken ct)
    {
        return Task.FromResult(Items.FirstOrDefault(d => d.Id == documentId));
    }

    public Task<HandoverDocument?> FindByTurnIdAsync(Guid turnId, CancellationToken ct)
    {
        return Task.FromResult(Items.FirstOrDefault(d => d.TurnId == turnId));
    }

    public Task<List<HandoverDocument>> ListByRecipientAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(Items.Where(d => d.Turn.ClaimantUserId == userId).OrderByDescending(d => d.CreatedAt).ToList());
    }

    public Task<List<HandoverDocument>> ListByVaultIdsAsync(List<Guid> vaultIds, CancellationToken ct)
    {
        return Task.FromResult(Items.Where(d => vaultIds.Contains(d.VaultId)).OrderByDescending(d => d.CreatedAt).ToList());
    }

    public Task<List<HandoverDocument>> ListForVerifierAsync(Guid verifierUserId, CancellationToken ct)
    {
        return Task.FromResult(Items
            .Where(d => VerifierVaults.Contains((verifierUserId, d.VaultId)))
            .OrderByDescending(d => d.CreatedAt).ToList());
    }
}

/// <summary>Notifier giả: ghi lại ai được báo về bước nào.</summary>
public sealed class FakeDocumentNotifier : IHandoverDocumentNotifier
{
    public List<string> Events { get; } = new List<string>();

    public Task VerifierTurnAsync(HandoverDocument document, CancellationToken ct)
    {
        Events.Add("verifier-turn");
        return Task.CompletedTask;
    }

    public Task RecipientReturnedAsync(HandoverDocument document, string reason, CancellationToken ct)
    {
        Events.Add("recipient-returned:" + reason);
        return Task.CompletedTask;
    }

    public Task CompletedAsync(HandoverDocument document, CancellationToken ct)
    {
        Events.Add("completed");
        return Task.CompletedTask;
    }
}

/// <summary>Bộ giải mã hồ sơ chứng tử giả: trả sẵn phần khai báo đặt trong test, không mã hóa gì.</summary>
public sealed class FakeDeathCaseIdentityProtector : IDeathCaseDataProtector
{
    public DeathCaseIdentityData? Identity { get; set; }

    public void Protect(DeathCaseVersion version, DeathCaseIdentityData data)
    {
        Identity = data;
    }

    public DeathCaseIdentityData? Unprotect(DeathCaseVersion version)
    {
        return Identity;
    }
}

/// <summary>Luồng Biên Bản: người nhận chọn (Phần 1 chép sẵn từ hồ sơ chứng tử), người nhận ký, Người xét duyệt kiểm tra và gửi.</summary>
public sealed class HandoverDocumentTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
    private static readonly byte[] Jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1 };

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository handover = new FakeHandoverRepository();
    private readonly FakeHandoverSubmissionRepository submissions = new FakeHandoverSubmissionRepository();
    private readonly FakeHandoverDocumentRepository documents = new FakeHandoverDocumentRepository();
    private readonly FakeEkycBeneficiaryRepository beneficiaries = new FakeEkycBeneficiaryRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
    private readonly InMemoryContentStore store = new InMemoryContentStore();
    private readonly FakeAuditLogRepository audit = new FakeAuditLogRepository();
    private readonly FakeHoldReader holds = new FakeHoldReader();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider time = new FixedTimeProvider(Now);
    private readonly FakeDeathCaseRepository deathCases = new FakeDeathCaseRepository();
    private readonly FakeDeathCaseIdentityProtector deathCaseProtector = new FakeDeathCaseIdentityProtector();
    private readonly FakeProgressExecutorReader executorReader = new FakeProgressExecutorReader();
    private readonly FakeDocumentNotifier notifier = new FakeDocumentNotifier();
    private readonly FakePaperRenderer renderer = new FakePaperRenderer();
    private readonly User recipient;
    private readonly User executor;
    private readonly User verifier;
    private readonly HandoverTurn turn;
    private readonly DeathCase deathCase;
    private readonly HandoverDocumentService documentService;
    private readonly HandoverSubmissionService submitService;
    private readonly HandoverSubmissionReviewService reviewService;

    public HandoverDocumentTests()
    {
        recipient = new User { Email = "nguoi-nhan@example.com", FullName = "Trần Minh Anh", Status = UserStatus.Active, EmailVerifiedAt = Now.AddDays(-10) };
        executor = new User { Email = "thuc-thi@example.com", FullName = "Lê Văn Thực", Status = UserStatus.Active, EmailVerifiedAt = Now.AddDays(-10) };
        verifier = new User { Email = "ls@example.com", FullName = "Luật sư Dũng", Role = UserRole.Verifier, Status = UserStatus.Active, EmailVerifiedAt = Now.AddDays(-10) };
        users.Users.Add(recipient);
        users.Users.Add(executor);
        users.Users.Add(verifier);
        users.Users.Add(data.Owner);
        vaults.Vaults.Add(data.Vault);

        Container container = data.AddContainer("Gói gia đình", ContainerStatus.Ready, 3);
        Designation designation = data.Designate(container, "Trần Minh Anh", "minh-anh@example.com");
        beneficiaries.Beneficiaries.Add(designation.Beneficiary);
        protector.Data[designation.BeneficiaryId] = data.People[designation.BeneficiaryId];

        turn = data.BuildTurn(container, designation, HandoverTurnStatus.IdentityInProgress, "token-" + Guid.NewGuid().ToString("N"), recipient.Id);
        handover.Turns.Add(turn);
        submissions.Approvers.Add((verifier.Id, data.Vault.Id));
        documents.VerifierVaults.Add((verifier.Id, data.Vault.Id));
        executorReader.Executors.Add(new Executor { VaultId = data.Vault.Id, UserId = executor.Id, Status = ExecutorStatus.Accepted });
        deathCases.ExecutorRows.Add(new ExecutorVaultRow { VaultId = data.Vault.Id, OwnerFullName = data.Owner.FullName });
        currentUser.UserId = recipient.Id;

        // Hồ sơ chứng tử đã duyệt: ngày mất, ngày cấp để rõ; họ tên và số giấy nằm trong gói đã mã hóa (ở đây là bộ giải mã giả).
        deathCase = new DeathCase
        {
            VaultId = data.Vault.Id,
            PlanId = data.Plan.Id,
            Status = DeathCaseStatus.ApprovedForHandover,
            CurrentVersionNo = 1,
            ReviewVersionNo = 1,
            CreatedAt = Now.AddDays(-5),
        };
        deathCase.Versions.Add(new DeathCaseVersion
        {
            CaseId = deathCase.Id,
            VersionNo = 1,
            DateOfDeath = new DateOnly(2026, 11, 5),
            IssuedOn = new DateOnly(2026, 11, 7),
            SubmittedAt = Now.AddDays(-4),
        });
        deathCases.Cases.Add(deathCase);
        deathCaseProtector.Identity = new DeathCaseIdentityData
        {
            OwnerFullName = "Nguyễn Văn Chủ Kho",
            CertificateNumber = "0456/2026",
        };

        HandoverPaperWriter writer = new HandoverPaperWriter(users, vaults, beneficiaries, protector, renderer, deathCases, store);
        documentService = new HandoverDocumentService(
            currentUser, handover, documents, vaults, users, executorReader, deathCases, deathCaseProtector, unitOfWork, time, notifier);
        submitService = new HandoverSubmissionService(
            currentUser, handover, submissions, beneficiaries, protector, store, unitOfWork, time, writer, documents, notifier);
        reviewService = new HandoverSubmissionReviewService(
            currentUser, users, vaults, submissions, holds, store, audit, unitOfWork, time, writer, handover, documents, notifier);
    }

    private sealed class FakePaperRenderer : IDeclinePaperRenderer
    {
        public DeclinePaperData? Last { get; private set; }

        public byte[] Render(DeclinePaperData paper)
        {
            Last = paper;
            return new byte[] { 0x25, 0x50, 0x44, 0x46 };
        }
    }

    private static CreateHandoverDocumentRequest Choose(string kind)
    {
        return new CreateHandoverDocumentRequest { Kind = kind };
    }

    private static SubmitHandoverSubmissionRequest RecipientPart(string kind)
    {
        return new SubmitHandoverSubmissionRequest
        {
            Kind = kind,
            FullName = "Trần Minh Anh",
            CitizenId = "012345671234",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Acknowledged = true,
            VoluntaryDecline = kind == "DECLINE",
        };
    }

    private static ApproveHandoverDocumentRequest AllChecks()
    {
        return new ApproveHandoverDocumentRequest { Checks = new List<bool> { true, true, true, true }, Acknowledged = true };
    }

    private async Task<HandoverDocumentResponse> Create(string kind)
    {
        currentUser.UserId = recipient.Id;
        return await documentService.CreateAsync(turn.Id, Choose(kind), CancellationToken.None);
    }

    private async Task<HandoverSubmission> RecipientSigns(string kind)
    {
        currentUser.UserId = recipient.Id;
        await submitService.SubmitAsync(turn.Id, RecipientPart(kind), Png, Jpeg, CancellationToken.None);
        HandoverSubmission submission = submissions.Items.Last();
        submission.Turn = turn;
        return submission;
    }

    // ---- Người nhận chọn: Phần 1 chép sẵn từ hồ sơ chứng tử ----

    [Fact]
    public async Task Create_GoesStraightToTheRecipientWithPart1CopiedFromTheApprovedDeathCase()
    {
        HandoverDocumentResponse response = await Create("ACCEPT");

        Assert.Equal("WAIT_RECIPIENT", response.Status);
        Assert.Equal("ACCEPT", response.Kind);
        Assert.Equal("Gói gia đình", response.ContainerName);
        Assert.Equal(3, response.ItemCount);
        Assert.Equal("Nguyễn Văn Chủ Kho", response.Owner.FullName);
        Assert.Equal(new DateOnly(2026, 11, 5), response.Owner.DateOfDeath);
        Assert.Equal("0456/2026", response.Owner.CertificateNumber);
        Assert.Equal(new DateOnly(2026, 11, 7), response.Owner.CertificateIssuedOn);
        Assert.Equal("Trần Minh Anh", response.Recipient.FullName);
        Assert.Null(response.Recipient.SignedAt);
        Assert.Single(documents.Items);
        Assert.Empty(notifier.Events);
    }

    [Fact]
    public async Task Create_UsesTheOwnerAccountNameAndLeavesBlanksWhenTheDeathCaseHasNoDetails()
    {
        deathCaseProtector.Identity = null;
        deathCase.Versions[0].DateOfDeath = null;
        deathCase.Versions[0].IssuedOn = null;

        HandoverDocumentResponse response = await Create("ACCEPT");

        Assert.Equal(data.Owner.FullName, response.Owner.FullName);
        Assert.Null(response.Owner.DateOfDeath);
        Assert.Null(response.Owner.CertificateIssuedOn);
        Assert.Equal(string.Empty, response.Owner.CertificateNumber);
        Assert.Equal("WAIT_RECIPIENT", response.Status);
    }

    [Fact]
    public async Task Create_WithoutAReviewedDeathCase_StillWorksWithBlankPart1()
    {
        deathCases.Cases.Clear();

        HandoverDocumentResponse response = await Create("ACCEPT");

        Assert.Equal(data.Owner.FullName, response.Owner.FullName);
        Assert.Null(response.Owner.DateOfDeath);
        Assert.Equal("WAIT_RECIPIENT", response.Status);
    }

    [Fact]
    public async Task Create_SameKindAgain_ReturnsTheSameDocument()
    {
        HandoverDocumentResponse first = await Create("ACCEPT");
        HandoverDocumentResponse second = await Create("ACCEPT");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(documents.Items);
    }

    [Fact]
    public async Task Create_OtherKindBeforeTheRecipientSigns_ChangesTheChoice()
    {
        await Create("ACCEPT");

        HandoverDocumentResponse changed = await Create("DECLINE");

        Assert.Equal("DECLINE", changed.Kind);
        Assert.Single(documents.Items);
    }

    [Fact]
    public async Task Create_OtherKindAfterTheRecipientSigned_IsRejected()
    {
        await Create("ACCEPT");
        await RecipientSigns("ACCEPT");

        AppException error = await Assert.ThrowsAsync<AppException>(() => Create("DECLINE"));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
    }

    [Fact]
    public async Task Create_ByAnotherUser_IsNotFound()
    {
        currentUser.UserId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => documentService.CreateAsync(turn.Id, Choose("ACCEPT"), CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
    }

    [Fact]
    public async Task Create_WhenTurnIsNotInIdentityStep_IsRejected()
    {
        turn.Status = HandoverTurnStatus.ReadyToAccept;

        AppException error = await Assert.ThrowsAsync<AppException>(() => Create("ACCEPT"));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
    }

    // ---- Người nhận ký ----

    [Fact]
    public async Task RecipientSubmit_StoresOnlyNameBirthYearAndLast4AndNotifiesVerifier()
    {
        await Create("ACCEPT");

        HandoverSubmission submission = await RecipientSigns("ACCEPT");

        HandoverDocument document = documents.Items[0];
        Assert.Equal(HandoverDocumentStatus.WaitVerifier, document.Status);
        Assert.Equal("Trần Minh Anh", document.RecipientFullName);
        Assert.Equal(1990, document.RecipientBirthYear);
        Assert.Equal("1234", document.RecipientCitizenIdLast4);
        Assert.Equal(submission.Id, document.SubmissionId);
        Assert.Equal(Now, document.RecipientSignedAt);
        Assert.Contains("verifier-turn", notifier.Events);
    }

    [Fact]
    public async Task RecipientSubmit_WithAnotherKindThanTheDocument_IsRejected()
    {
        await Create("ACCEPT");

        await Assert.ThrowsAsync<AppException>(() => RecipientSigns("DECLINE"));
    }

    [Fact]
    public async Task RecipientSubmit_AgainWhileWaitingForTheVerifier_IsRejected()
    {
        await Create("ACCEPT");
        await RecipientSigns("ACCEPT");

        AppException error = await Assert.ThrowsAsync<AppException>(() => RecipientSigns("ACCEPT"));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Single(submissions.Items);
    }

    // ---- Người xét duyệt gửi ----

    private async Task<HandoverSubmission> ReadyForVerifier(string kind)
    {
        await Create(kind);
        HandoverSubmission submission = await RecipientSigns(kind);
        currentUser.UserId = verifier.Id;
        return submission;
    }

    [Fact]
    public async Task Complete_Accept_OpensTheGrantEndsTheTurnAndDeletesIdentityImages()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");
        int contentsBefore = store.Contents.Count;

        VerifierHandoverSubmissionResponse result = await reviewService.CompleteAsync(submission.Id, AllChecks(), CancellationToken.None);

        HandoverDocument document = documents.Items[0];
        Assert.Equal(HandoverDocumentStatus.Completed, document.Status);
        Assert.Equal("1111", document.VerifierChecks);
        Assert.Equal(verifier.Id, document.VerifierUserId);
        Assert.Equal(Now, document.VerifierSignedAt);
        Assert.Equal(HandoverTurnStatus.HandoverCommitted, turn.Status);
        HandoverGrant grant = Assert.Single(handover.Grants);
        Assert.Equal(recipient.Id, grant.UserId);
        Assert.Equal(HandoverGrantStatus.Active, grant.Status);
        Assert.Equal(Now + HandoverRules.GrantWindow, grant.ExpiresAt);
        Assert.Equal(HandoverSubmissionStatus.Approved, submission.Status);
        Assert.Empty(submission.FrontImageStorageKey);
        Assert.Empty(submission.BackImageStorageKey);
        Assert.NotEmpty(submission.PaperStorageKey);
        Assert.Equal(contentsBefore - 2 + 1, store.Contents.Count);
        Assert.Equal(PlanStatus.Closed, data.Plan.Status);
        Assert.Equal(document.Id, result.DocumentId);
        Assert.Contains("completed", notifier.Events);
    }

    [Fact]
    public async Task Complete_PrintsPart1FromTheDocumentAndOnlyTwoSignatures()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");

        await reviewService.CompleteAsync(submission.Id, AllChecks(), CancellationToken.None);

        DeclinePaperData paper = Assert.IsType<DeclinePaperData>(renderer.Last);
        Assert.Equal("Nguyễn Văn Chủ Kho", paper.OwnerFullName);
        Assert.Equal("0456/2026", paper.CertificateNumber);
        Assert.Equal(new DateOnly(2026, 11, 5), paper.DateOfDeath);
        Assert.Equal(new DateOnly(2026, 11, 7), paper.DeathCertificateIssuedOn);
        Assert.Equal("Trần Minh Anh", paper.DeclinerFullName);
        Assert.Equal(1990, paper.RecipientBirthYear);
        Assert.Equal(3, paper.ItemCount);
        Assert.True(paper.HasSignatures);
        Assert.Equal(Now, paper.RecipientSignedAt);
        Assert.Equal("Luật sư Dũng", paper.ReviewerFullName);
    }

    [Fact]
    public async Task Complete_Decline_EndsTheTurnAsDeclinedWithoutGrant()
    {
        HandoverSubmission submission = await ReadyForVerifier("DECLINE");

        await reviewService.CompleteAsync(submission.Id, AllChecks(), CancellationToken.None);

        Assert.Equal(HandoverTurnStatus.Declined, turn.Status);
        Assert.Empty(handover.Grants);
        Assert.Equal(HandoverDocumentStatus.Completed, documents.Items[0].Status);
    }

    [Fact]
    public async Task Complete_NeedsAllFourChecksAndTheResponsibilityBox()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");
        ApproveHandoverDocumentRequest threeChecks = new ApproveHandoverDocumentRequest { Checks = new List<bool> { true, true, false, true }, Acknowledged = true };
        ApproveHandoverDocumentRequest noAck = new ApproveHandoverDocumentRequest { Checks = new List<bool> { true, true, true, true }, Acknowledged = false };

        await Assert.ThrowsAsync<AppException>(() => reviewService.CompleteAsync(submission.Id, threeChecks, CancellationToken.None));
        await Assert.ThrowsAsync<AppException>(() => reviewService.CompleteAsync(submission.Id, noAck, CancellationToken.None));

        Assert.Equal(HandoverDocumentStatus.WaitVerifier, documents.Items[0].Status);
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
        Assert.Empty(handover.Grants);
    }

    [Fact]
    public async Task Complete_WhenSubmissionDoesNotBelongToTheVerifier_IsNotFound()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");
        User other = new User { Email = "khac@example.com", FullName = "Khác", Role = UserRole.Verifier, Status = UserStatus.Active, EmailVerifiedAt = Now.AddDays(-1) };
        users.Users.Add(other);
        currentUser.UserId = other.Id;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => reviewService.CompleteAsync(submission.Id, AllChecks(), CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
    }

    [Fact]
    public async Task Complete_Twice_ReturnsTheSameResultWithoutASecondGrant()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");
        await reviewService.CompleteAsync(submission.Id, AllChecks(), CancellationToken.None);

        await reviewService.CompleteAsync(submission.Id, AllChecks(), CancellationToken.None);

        Assert.Single(handover.Grants);
    }

    [Fact]
    public async Task Complete_WhileTheVaultIsOnHold_IsRejected()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");
        holds.Active = true;

        await Assert.ThrowsAsync<AppException>(() => reviewService.CompleteAsync(submission.Id, AllChecks(), CancellationToken.None));

        Assert.Empty(handover.Grants);
    }

    [Fact]
    public async Task LegacyApprove_IsBlockedForASubmissionThatBelongsToADocument()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => reviewService.ApproveAsync(submission.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    // ---- Trả về để bổ sung ----

    [Fact]
    public async Task Supplement_ReturnsTheDocumentToTheRecipientKeepingPart1_AndResubmitGoesBackToVerifier()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");

        await reviewService.RequestSupplementAsync(
            submission.Id, new RequestHandoverSupplementRequest { Reason = "Ảnh mặt sau bị mờ" }, CancellationToken.None);

        HandoverDocument document = documents.Items[0];
        Assert.Equal(HandoverDocumentStatus.WaitRecipient, document.Status);
        Assert.Equal("Ảnh mặt sau bị mờ", document.SupplementReason);
        Assert.Null(document.RecipientSignedAt);
        Assert.Equal("0456/2026", document.CertificateNumber);
        Assert.Equal("Nguyễn Văn Chủ Kho", document.OwnerFullName);

        await RecipientSigns("ACCEPT");
        Assert.Equal(HandoverDocumentStatus.WaitVerifier, document.Status);
        Assert.Null(document.SupplementReason);
    }

    [Fact]
    public async Task Supplement_AfterBeingReturned_TheRecipientCannotSwitchBetweenAcceptAndDecline()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");
        await reviewService.RequestSupplementAsync(
            submission.Id, new RequestHandoverSupplementRequest { Reason = "Ảnh mặt sau bị mờ" }, CancellationToken.None);

        AppException error = await Assert.ThrowsAsync<AppException>(() => Create("DECLINE"));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
    }

    // ---- Danh sách và Người thực thi xem lại ----

    [Fact]
    public async Task Lists_ShowTheDocumentToTheRightPeopleAndOnlyTheVerifierGetsTheSubmissionId()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");

        currentUser.UserId = recipient.Id;
        List<HandoverDocumentResponse> mine = await documentService.ListMineAsync(CancellationToken.None);
        currentUser.UserId = executor.Id;
        List<HandoverDocumentResponse> forExecutor = await documentService.ListForExecutorAsync(CancellationToken.None);
        currentUser.UserId = verifier.Id;
        List<HandoverDocumentResponse> forVerifier = await documentService.ListForVerifierAsync(CancellationToken.None);
        currentUser.UserId = Guid.NewGuid();
        List<HandoverDocumentResponse> stranger = await documentService.ListMineAsync(CancellationToken.None);

        Assert.Single(mine);
        Assert.Single(forExecutor);
        Assert.Single(forVerifier);
        Assert.Empty(stranger);
        Assert.Null(mine[0].SubmissionId);
        Assert.Null(forExecutor[0].SubmissionId);
        Assert.Equal(submission.Id, forVerifier[0].SubmissionId);
        Assert.Equal("WAIT_VERIFIER", forVerifier[0].Status);
    }

    [Fact]
    public async Task Executor_CanReadTheDocumentButOwnerAndStrangerGetNotFound()
    {
        HandoverDocumentResponse created = await Create("ACCEPT");

        currentUser.UserId = executor.Id;
        HandoverDocumentResponse seen = await documentService.GetForExecutorAsync(created.Id, CancellationToken.None);
        currentUser.UserId = data.Owner.Id;
        AppException asOwner = await Assert.ThrowsAsync<AppException>(
            () => documentService.GetForExecutorAsync(created.Id, CancellationToken.None));
        currentUser.UserId = Guid.NewGuid();
        AppException asStranger = await Assert.ThrowsAsync<AppException>(
            () => documentService.GetForExecutorAsync(created.Id, CancellationToken.None));

        Assert.Equal(created.Id, seen.Id);
        Assert.Equal("0456/2026", seen.Owner.CertificateNumber);
        Assert.Equal(ErrorCode.HandoverNotFound, asOwner.Code);
        Assert.Equal(ErrorCode.HandoverNotFound, asStranger.Code);
    }

    [Fact]
    public async Task ListForVerifier_ByANonVerifier_IsForbidden()
    {
        currentUser.UserId = recipient.Id;

        AppException error = await Assert.ThrowsAsync<AppException>(() => documentService.ListForVerifierAsync(CancellationToken.None));

        Assert.Equal(ErrorCode.RoleNotAllowed, error.Code);
    }

    [Fact]
    public async Task CompletedDocument_StaysListedForever()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");
        await reviewService.CompleteAsync(submission.Id, AllChecks(), CancellationToken.None);

        currentUser.UserId = recipient.Id;
        List<HandoverDocumentResponse> mine = await documentService.ListMineAsync(CancellationToken.None);

        Assert.Equal("COMPLETED", Assert.Single(mine).Status);
        Assert.NotNull(mine[0].CompletedAt);
    }

    [Fact]
    public async Task CompletedDocument_KeepsPart1EvenIfTheDeathCaseIsGoneLater()
    {
        HandoverSubmission submission = await ReadyForVerifier("ACCEPT");
        await reviewService.CompleteAsync(submission.Id, AllChecks(), CancellationToken.None);
        deathCases.Cases.Clear();
        deathCaseProtector.Identity = null;

        currentUser.UserId = recipient.Id;
        List<HandoverDocumentResponse> mine = await documentService.ListMineAsync(CancellationToken.None);

        Assert.Equal("Nguyễn Văn Chủ Kho", mine[0].Owner.FullName);
        Assert.Equal("0456/2026", mine[0].Owner.CertificateNumber);
        Assert.Equal(new DateOnly(2026, 11, 5), mine[0].Owner.DateOfDeath);
    }
}
