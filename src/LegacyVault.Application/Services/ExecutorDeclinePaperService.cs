using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IExecutorDeclinePaperService
{
    /// <summary>Người thực thi tải PDF văn bản từ chối của một lượt đã bị người nhận từ chối.</summary>
    Task<PreparedDownload> PrepareAsync(Guid caseId, Guid turnId, CancellationToken ct);
}

public sealed class ExecutorDeclinePaperService : IExecutorDeclinePaperService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverProgressReader progressReader;
    private readonly IHandoverRepository handover;
    private readonly IHandoverSubmissionRepository submissions;
    private readonly IAssetContentStore contentStore;

    public ExecutorDeclinePaperService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IExecutorReader executorReader,
        IHandoverProgressReader progressReader,
        IHandoverRepository handover,
        IHandoverSubmissionRepository submissions,
        IAssetContentStore contentStore)
    {
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.executorReader = executorReader;
        this.progressReader = progressReader;
        this.handover = handover;
        this.submissions = submissions;
        this.contentStore = contentStore;
    }

    public async Task<PreparedDownload> PrepareAsync(Guid caseId, Guid turnId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DeathCase? deathCase = await progressReader.FindCaseAsync(caseId, ct);
        Vault? vault = deathCase == null ? null : await vaults.FindByIdAsync(deathCase.VaultId, ct);
        if (deathCase == null || vault == null || !await IsActiveExecutorAsync(vault, userId, ct))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.DeathCaseNotFound, "Không tìm thấy hồ sơ chứng tử.");
        }

        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.PlanId != deathCase.PlanId || turn.VaultId != vault.Id
            || turn.Status != HandoverTurnStatus.Declined)
        {
            throw PaperNotFound();
        }

        HandoverSubmission? submission = await submissions.FindLatestByTurnAsync(turn.Id, ct);
        if (submission == null || submission.Kind != HandoverSubmissionKind.Decline
            || submission.Status != HandoverSubmissionStatus.Approved || submission.PaperStorageKey.Length == 0)
        {
            throw PaperNotFound();
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = submission.PaperStorageKey;
        stored.SizeBytes = submission.PaperSizeBytes;
        stored.EncryptionKeyId = submission.PaperEncryptionKeyId;
        stored.WrappedDataKey = submission.PaperWrappedDataKey;
        return await IdentityImageStorage.PrepareAsync(contentStore, stored, "application/pdf", "van-ban-tu-choi.pdf", ct);
    }

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

    private static AppException PaperNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không có văn bản từ chối cho lượt này.");
    }
}
