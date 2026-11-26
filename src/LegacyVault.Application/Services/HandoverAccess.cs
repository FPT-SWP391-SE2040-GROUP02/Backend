using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// Các bước kiểm quyền và các lỗi dùng chung của phần bàn giao. Trước đây mỗi service chép một bản giống hệt;
/// gom về đây để sửa một chỗ là đúng cho mọi nơi.
/// </summary>
public static class HandoverAccess
{
    /// <summary>Lượt của người đang đăng nhập; id lạ và lượt của người khác cho cùng một lỗi không tìm thấy.</summary>
    public static async Task<HandoverTurn> FindMyTurnAsync(
        IHandoverRepository handover, Guid turnId, Guid userId, CancellationToken ct)
    {
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.ClaimantUserId != userId)
        {
            throw NotFound();
        }
        return turn;
    }

    /// <summary>Người dùng là Người thực thi đang hiệu lực của kho. Chủ tài sản không bao giờ được tính là Người thực thi của chính kho mình.</summary>
    public static async Task<bool> IsActiveExecutorAsync(
        IExecutorReader executorReader, Vault vault, Guid userId, CancellationToken ct)
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

    /// <summary>
    /// Biên bản bàn giao và kho cho các màn của Người thực thi. Không thấy biên bản, không thấy kho, hoặc người gọi không phải
    /// Người thực thi đang hiệu lực thì cùng một lỗi không tìm thấy hồ sơ, để không lộ hồ sơ nào có thật.
    /// Hàm dùng chung cho ba service nên nhận sẵn ba bộ đọc dữ liệu mà các service này đều đã có, để không chép lại phần kiểm.
    /// </summary>
    public static async Task<ExecutorCaseAccess> FindCaseForExecutorAsync(
        IHandoverProgressReader progressReader, IVaultRepository vaults, IExecutorReader executorReader,
        Guid caseId, Guid userId, CancellationToken ct)
    {
        HandoverRecord? record = await progressReader.FindRecordAsync(caseId, ct);
        if (record == null)
        {
            throw DeathCaseNotFound();
        }
        Vault? vault = await vaults.FindByIdAsync(record.VaultId, ct);
        if (vault == null || !await IsActiveExecutorAsync(executorReader, vault, userId, ct))
        {
            throw DeathCaseNotFound();
        }
        ExecutorCaseAccess access = new ExecutorCaseAccess
        {
            Record = record,
            Vault = vault
        };
        return access;
    }

    /// <summary>Không tìm thấy lượt, quyền tải hay tài sản bàn giao (dùng chung cho cả trường hợp không có quyền xem).</summary>
    public static AppException NotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy nội dung bàn giao.");
    }

    /// <summary>Không tìm thấy biên bản bàn giao, hoặc người gọi không phải Người thực thi của kho.</summary>
    public static AppException DeathCaseNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy biên bản bàn giao.");
    }

    /// <summary>Thao tác không được phép ở trạng thái hiện tại; reason là mã lý do để giao diện hiện câu phù hợp.</summary>
    public static AppException NotAllowed(string reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(AppErrorType.Conflict, ErrorCode.HandoverNotAllowed, message, extensions);
    }
}
