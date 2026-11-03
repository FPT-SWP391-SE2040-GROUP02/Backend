using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Tracked invitation lookup; raw tokens never reach persistence.</summary>
public interface IExecutorInvitationRepository
{
    Task<Executor?> FindByTokenHashAsync(string hash, CancellationToken ct);
    void Add(Executor executor);

    Task<Executor?> FindByIdAsync(Guid executorId, CancellationToken ct);

    /// <summary>
    /// Lời mời gửi tới một email (đã chuẩn hóa chữ thường), kèm Vault và Owner để hiện tên người mời.
    /// Chỉ đọc; mới nhất trước, tối đa maxCount dòng.
    /// </summary>
    Task<List<Executor>> ListByInviteeEmailAsync(string normalizedEmail, int maxCount, CancellationToken ct);

    /// <summary>Id lời mời Invited đã quá hạn, hoặc đã qua mốc nhắc mà chưa nhắc. Cũ nhất trước.</summary>
    Task<List<Guid>> FindDueForJobAsync(DateTimeOffset now, DateTimeOffset remindInvitedBefore, int maxCount, CancellationToken ct);
}
