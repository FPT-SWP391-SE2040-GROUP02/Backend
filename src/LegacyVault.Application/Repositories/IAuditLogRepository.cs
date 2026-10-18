using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng AuditLogs. Chỉ thêm dòng: không có hàm sửa hay xóa (ADMIN-06, DATA-07).</summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Đưa dòng nhật ký vào cùng lần lưu với thao tác chính: người gọi gọi IUnitOfWork.SaveChangesAsync một lần,
    /// nên thao tác lỗi thì nhật ký cũng không được ghi (STATE-02).
    /// </summary>
    void Add(AuditLog log);

    /// <summary>
    /// Một trang nhật ký của kho (không theo dõi thay đổi), kèm tổng số dòng khớp bộ lọc.
    /// action null = mọi loại; from/to null = không giới hạn, from tính cả mốc, to không tính mốc.
    /// Sắp theo At rồi Id (cùng chiều) để chia trang ổn định. pageIndex bắt đầu từ 1.
    /// </summary>
    Task<(List<AuditLog> Items, int TotalCount)> FindPageByVaultIdAsync(
        Guid vaultId, AuditAction? action, DateTimeOffset? from, DateTimeOffset? to,
        int pageIndex, int pageSize, bool isAscending, CancellationToken ct);
}
