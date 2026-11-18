using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Người thực thi tải ảnh biên bản đồng ý hoặc văn bản từ chối đã duyệt của một lượt.</summary>
public interface IExecutorHandoverPaperService
{
    /// <summary>Người thực thi xem ảnh văn bản từ chối (lượt bị từ chối) hoặc biên bản đồng ý (lượt đã đồng ý nhận).</summary>
    Task<PreparedDownload> PrepareAsync(Guid caseId, Guid turnId, CancellationToken ct);
}

/// <summary>Người thực thi tải ảnh biên bản đồng ý hoặc văn bản từ chối đã duyệt của một lượt; giải mã thẳng ra response.</summary>
public sealed class ExecutorHandoverPaperService : IExecutorHandoverPaperService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverProgressReader progressReader;
    private readonly IHandoverRepository handover;
    private readonly IHandoverSubmissionRepository submissions;
    private readonly IAssetContentStore contentStore;

    public ExecutorHandoverPaperService(
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
        ExecutorCaseAccess access = await HandoverAccess.FindCaseForExecutorAsync(
            progressReader, vaults, executorReader, caseId, userId, ct);
        DeathCase deathCase = access.DeathCase;
        Vault vault = access.Vault;

        // 2. Lượt phải thuộc kho này và đã kết thúc bằng đồng ý hoặc từ chối
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.PlanId != deathCase.PlanId || turn.VaultId != vault.Id
            || (turn.Status != HandoverTurnStatus.Declined && turn.Status != HandoverTurnStatus.HandoverCommitted))
        {
            throw PaperNotFound();
        }

        // 3. Hồ sơ mới nhất phải đã duyệt, đúng loại với kết quả lượt và đã có ảnh văn bản
        HandoverSubmission? submission = await submissions.FindLatestByTurnAsync(turn.Id, ct);
        HandoverSubmissionKind expected = HandoverSubmissionKind.Accept;
        string fileName = "bien-ban-dong-y.png";
        if (turn.Status == HandoverTurnStatus.Declined)
        {
            expected = HandoverSubmissionKind.Decline;
            fileName = "van-ban-tu-choi.png";
        }
        if (submission == null || submission.Kind != expected
            || submission.Status != HandoverSubmissionStatus.Approved || submission.PaperStorageKey.Length == 0)
        {
            throw PaperNotFound();
        }

        // 4. Chuẩn bị giải mã ảnh văn bản để tải về
        StoredContent stored = new StoredContent
        {
            StorageKey = submission.PaperStorageKey,
            SizeBytes = submission.PaperSizeBytes,
            EncryptionKeyId = submission.PaperEncryptionKeyId,
            WrappedDataKey = submission.PaperWrappedDataKey
        };
        return await IdentityImageStorage.PrepareAsync(contentStore, stored, "image/png", fileName, ct);
    }

    /// <summary>Lỗi 404 dùng chung cho lượt không thuộc kho, hồ sơ chưa duyệt hoặc chưa có ảnh văn bản.</summary>
    private static AppException PaperNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không có văn bản hoặc biên bản cho lượt này.");
    }
}
