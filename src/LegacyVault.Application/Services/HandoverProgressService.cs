using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using System.Security.Cryptography;

namespace LegacyVault.Application.Services;

/// <summary>Màn tiến độ bàn giao của Người thực thi: tình trạng từng lượt của từng người nhận.</summary>
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

    // Màn tiến độ bàn giao của Người thực thi: tình trạng từng lượt của từng người nhận.
    public async Task<HandoverProgressResponse> GetProgressAsync(Guid caseId, CancellationToken ct)
    {
        // 1. Tìm biên bản bàn giao và kho; người gọi phải là Người thực thi đang phụ trách kho
        Guid userId = currentUser.RequireUserId();
        ExecutorCaseAccess access = await HandoverAccess.FindCaseForExecutorAsync(
            progressReader, vaultRepository, executorReader, caseId, userId, ct);
        HandoverRecord record = access.Record;
        Vault vault = access.Vault;

        // 2. Biên bản chưa chốt lần nào thì chưa có lượt nào để theo dõi
        HandoverProgressResponse response = new HandoverProgressResponse
        {
            CaseId = record.Id,
            ServerTime = timeProvider.GetUtcNow()
        };

        // Lượt bàn giao chỉ được tạo khi luật sư chốt biên bản; trước đó chưa có gì để theo dõi.
        if (record.Status == HandoverRecordStatus.Drafting && record.FinalizedAt == null && record.RevisionNo == 1)
        {
            response.HandoverStarted = false;
            return response;
        }
        response.HandoverStarted = true;

        // 3. Lấy mọi lượt của biên bản, rồi đọc gộp một lần: lần tải đầu, 4 số cuối căn cước
        List<HandoverTurn> turns = new List<HandoverTurn>();
        foreach (HandoverTurn candidate in await progressReader.FindTurnsWithItemsAsync(record.PlanId, ct))
        {
            if (candidate.RecordId == record.Id)
            {
                turns.Add(candidate);
            }
        }
        List<Guid> grantIds = new List<Guid>();
        List<Guid> beneficiaryIds = new List<Guid>();
        List<Guid> turnIds = new List<Guid>();
        foreach (HandoverTurn turn in turns)
        {
            turnIds.Add(turn.Id);
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
        Dictionary<Guid, string> citizenIds = await progressReader.FindCitizenIdsAsync(beneficiaryIds, ct);

        // 4. Dựng từng dòng lượt; lượt còn hiệu lực hoặc đã bàn giao thì kèm liên hệ người nhận; cộng số đếm theo nhóm
        foreach (HandoverTurn turn in turns)
        {
            string citizenId = string.Empty;
            if (citizenIds.ContainsKey(turn.BeneficiaryId))
            {
                citizenId = citizenIds[turn.BeneficiaryId];
            }
            HandoverProgressTurnResponse turnResponse = turn.ToProgressResponse(citizenId, firstDownloads);
            turnResponse.ClaimedByYou = turn.ClaimantUserId == userId;
            HandoverRecordSignature? signature = HandoverRecordRules.FindSignature(
                record, HandoverSignerRole.Recipient, turn.BeneficiaryId);
            if (signature != null)
            {
                turnResponse.RecipientSignedAt = signature.SignedAt;
            }
            turnResponse.DocumentApprovedAt = record.CompletedAt;
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
    /// Giải mã họ tên, số điện thoại và email người nhận cho Executor. Không giải mã được (thiếu KEK, dữ liệu hỏng)
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
            turnResponse.BeneficiaryEmail = personal.Email;
        }
        catch (CryptographicException)
        {
            // Giữ nguyên null: màn hình hiện "chưa có".
        }
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
}
