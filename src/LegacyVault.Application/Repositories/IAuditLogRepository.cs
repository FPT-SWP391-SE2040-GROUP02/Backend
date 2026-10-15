using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng AuditLogs. Chỉ thêm dòng: không có hàm sửa hay xóa (ADMIN-06, DATA-07).</summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Đưa dòng nhật ký vào cùng lần lưu với thao tác chính: người gọi gọi IUnitOfWork.SaveChangesAsync một lần,
    /// nên thao tác lỗi thì nhật ký cũng không được ghi (STATE-02).
    /// </summary>
    void Add(AuditLog log);
}
