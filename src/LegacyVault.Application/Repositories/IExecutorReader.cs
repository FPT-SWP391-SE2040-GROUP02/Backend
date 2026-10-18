using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>
/// Đọc danh sách Executor của một kho cho các phần khác dùng (nộp chứng tử, bàn giao cho người nhận).
/// </summary>
/// <remarks>Chỉ đọc. Việc mời, chấp nhận, thu hồi Executor nằm ở phần thiết lập bàn giao.</remarks>
public interface IExecutorReader
{
    /// <summary>
    /// Các Executor đang hiệu lực (đã chấp nhận) của kho: Executor chính đứng trước, rồi tới dự phòng.
    /// Kết quả chỉ để đọc. Kho chưa có ai chấp nhận thì trả danh sách rỗng.
    /// </summary>
    Task<List<Executor>> GetActiveAsync(Guid vaultId, CancellationToken ct);
}
