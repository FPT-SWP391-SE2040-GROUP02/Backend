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

    /// <summary>
    /// ASSIGN-02: Executor nhận được việc lúc này = đã chấp nhận và tài khoản đang hoạt động (không bị khóa/vô hiệu hóa).
    /// Executor chính đứng trước, rồi tới dự phòng. Dùng khi chọn người nhận việc; danh sách loại trừ (ROLE-03) vẫn dùng GetActiveAsync.
    /// </summary>
    Task<List<Executor>> GetAvailableAsync(Guid vaultId, CancellationToken ct);
}
