using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IExecutorDeclinePaperService
{
    /// <summary>Người thực thi xem ảnh văn bản từ chối (lượt bị từ chối) hoặc biên bản đồng ý (lượt đã đồng ý nhận).</summary>
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

    // Người thực thi tải ảnh biên bản đồng ý hoặc văn bản từ chối đã duyệt của một lượt.
    public async Task<PreparedDownload> PrepareAsync(Guid caseId, Guid turnId, CancellationToken ct)
    {
        // 1. Tìm hồ sơ chứng tử và kho; người gọi phải là Người thực thi đang phụ trách kho
        Guid userId = currentUser.RequireUserId();
        DeathCase? deathCase = await progressReader.FindCaseAsync(caseId, ct);
        Vault? vault = deathCase == null ? null : await vaults.FindByIdAsync(deathCase.VaultId, ct);
        if (deathCase == null || vault == null || !await HandoverAccess.IsActiveExecutorAsync(executorReader, vault, userId, ct))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.DeathCaseNotFound, "Không tìm thấy hồ sơ chứng tử.");
        }

        // 2. Lượt phải thuộc kho này và đã kết thúc bằng đồng ý hoặc từ chối
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.PlanId != deathCase.PlanId || turn.VaultId != vault.Id
            || (turn.Status != HandoverTurnStatus.Declined && turn.Status != HandoverTurnStatus.HandoverCommitted))
        {
            throw PaperNotFound();
        }

        // 3. Hồ sơ mới nhất phải đã duyệt, đúng loại với kết quả lượt và đã có ảnh văn bản
        HandoverSubmission? submission = await submissions.FindLatestByTurnAsync(turn.Id, ct);
        HandoverSubmissionKind expected = turn.Status == HandoverTurnStatus.Declined
            ? HandoverSubmissionKind.Decline
            : HandoverSubmissionKind.Accept;
        if (submission == null || submission.Kind != expected
            || submission.Status != HandoverSubmissionStatus.Approved || submission.PaperStorageKey.Length == 0)
        {
            throw PaperNotFound();
        }

        // 4. Chuẩn bị giải mã ảnh văn bản để tải về
        StoredContent stored = new StoredContent();
        stored.StorageKey = submission.PaperStorageKey;
        stored.SizeBytes = submission.PaperSizeBytes;
        stored.EncryptionKeyId = submission.PaperEncryptionKeyId;
        stored.WrappedDataKey = submission.PaperWrappedDataKey;
        return await IdentityImageStorage.PrepareAsync(contentStore, stored, "image/png", expected == HandoverSubmissionKind.Decline ? "van-ban-tu-choi.png" : "bien-ban-dong-y.png", ct);
    }

    private static AppException PaperNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không có văn bản hoặc biên bản cho lượt này.");
    }
}
