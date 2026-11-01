using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using System.Security.Cryptography;

namespace LegacyVault.Application.Services;

public sealed class HandoverProgressService : IHandoverProgressService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverProgressReader progressReader;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector beneficiaryProtector;
    private readonly TimeProvider timeProvider;

    public HandoverProgressService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IExecutorReader executorReader,
        IHandoverProgressReader progressReader,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector beneficiaryProtector,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.executorReader = executorReader;
        this.progressReader = progressReader;
        this.beneficiaries = beneficiaries;
        this.beneficiaryProtector = beneficiaryProtector;
        this.timeProvider = timeProvider;
    }

    public async Task<HandoverProgressResponse> GetProgressAsync(Guid caseId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DeathCase? deathCase = await progressReader.FindCaseAsync(caseId, ct);
        if (deathCase == null)
        {
            throw CaseNotFound();
        }
        Vault? vault = await vaultRepository.FindByIdAsync(deathCase.VaultId, ct);
        if (vault == null || !await IsActiveExecutorAsync(vault, userId, ct))
        {
            throw CaseNotFound();
        }

        HandoverProgressResponse response = new HandoverProgressResponse();
        response.CaseId = deathCase.Id;
        response.ServerTime = timeProvider.GetUtcNow();

        // Lượt bàn giao chỉ được tạo khi Verifier duyệt; trước đó chưa có gì để theo dõi.
        if (deathCase.Status != DeathCaseStatus.ApprovedForHandover)
        {
            response.HandoverStarted = false;
            return response;
        }
        response.HandoverStarted = true;

        List<HandoverTurn> turns = await progressReader.FindTurnsWithItemsAsync(deathCase.PlanId, ct);
        List<Guid> grantIds = new List<Guid>();
        List<Guid> beneficiaryIds = new List<Guid>();
        foreach (HandoverTurn turn in turns)
        {
            if (turn.Grant != null)
            {
                grantIds.Add(turn.Grant.Id);
            }
            if (!beneficiaryIds.Contains(turn.BeneficiaryId))
            {
                beneficiaryIds.Add(turn.BeneficiaryId);
            }
        }
        Dictionary<Guid, DateTimeOffset> firstDownloads = await progressReader.FindFirstDownloadTimesAsync(grantIds, ct);
        Dictionary<Guid, string> citizenIdLast4 = await progressReader.FindCitizenIdLast4Async(beneficiaryIds, ct);

        foreach (HandoverTurn turn in turns)
        {
            string last4 = string.Empty;
            if (citizenIdLast4.ContainsKey(turn.BeneficiaryId))
            {
                last4 = citizenIdLast4[turn.BeneficiaryId];
            }
            HandoverProgressTurnResponse turnResponse = turn.ToProgressResponse(last4, firstDownloads);
            turnResponse.ClaimedByYou = turn.ClaimantUserId == userId;
            if (ShowsBeneficiaryContact(turn.Status))
            {
                await FillBeneficiaryContactAsync(turnResponse, vault.Id, turn.BeneficiaryId, ct);
            }
            response.Turns.Add(turnResponse);
            CountTurn(response.Summary, turn.Status);
        }
        return response;
    }

    /// <summary>
    /// Lượt còn hiệu lực (đang mời, đang xác minh, chờ quyết định) hoặc đã bàn giao: Executor cần tên và số điện thoại
    /// để liên hệ nhắc người nhận. Lượt đã kết thúc (từ chối, hết hạn, hủy, chấm dứt) thì không trả nữa.
    /// </summary>
    private static bool ShowsBeneficiaryContact(HandoverTurnStatus status)
    {
        return status == HandoverTurnStatus.Invited
            || status == HandoverTurnStatus.InitialResponseExpired
            || status == HandoverTurnStatus.IdentityInProgress
            || status == HandoverTurnStatus.ReadyToAccept
            || status == HandoverTurnStatus.HandoverCommitted;
    }

    /// <summary>
    /// Giải mã họ tên và số điện thoại người nhận cho Executor. Không giải mã được (thiếu KEK, dữ liệu hỏng)
    /// thì để trống thay vì làm hỏng cả màn hình tiến độ.
    /// </summary>
    private async Task FillBeneficiaryContactAsync(
        HandoverProgressTurnResponse turnResponse, Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(vaultId, beneficiaryId, ct);
        if (beneficiary == null)
        {
            return;
        }
        try
        {
            BeneficiaryPersonalData personal = beneficiaryProtector.Unprotect(beneficiary);
            turnResponse.BeneficiaryFullName = personal.FullName;
            turnResponse.BeneficiaryPhone = personal.Phone;
        }
        catch (CryptographicException)
        {
            // Giữ nguyên null: màn hình hiện "chưa có".
        }
    }

    /// <summary>Executor đang hiệu lực của kho và không phải Owner, giống luật của phần nộp hồ sơ chứng tử.</summary>
    private async Task<bool> IsActiveExecutorAsync(Vault vault, Guid userId, CancellationToken ct)
    {
        if (vault.OwnerId == userId)
        {
            return false;
        }
        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == userId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Gom trạng thái lượt vào năm nhóm hiển thị trên màn hình tổng hợp.</summary>
    private static void CountTurn(HandoverProgressSummaryResponse summary, HandoverTurnStatus status)
    {
        summary.TotalTurns++;
        if (status == HandoverTurnStatus.Invited || status == HandoverTurnStatus.InitialResponseExpired)
        {
            summary.WaitingResponse++;
        }
        else if (status == HandoverTurnStatus.IdentityInProgress || status == HandoverTurnStatus.ReadyToAccept)
        {
            summary.InVerification++;
        }
        else if (status == HandoverTurnStatus.HandoverCommitted)
        {
            summary.Accepted++;
        }
        else if (status == HandoverTurnStatus.Declined)
        {
            summary.Declined++;
        }
        else
        {
            summary.Ended++;
        }
    }

    private static AppException CaseNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.DeathCaseNotFound, "Không tìm thấy hồ sơ chứng tử.");
    }
}
