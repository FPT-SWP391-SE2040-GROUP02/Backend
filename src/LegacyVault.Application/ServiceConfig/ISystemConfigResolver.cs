using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.ServiceConfig;

/// <summary>
/// Cấu hình dịch vụ ngoài lúc chạy: dòng DB (Admin sửa trên web) đè appsettings. Cài đặt ở Infrastructure.
/// </summary>
public interface ISystemConfigResolver
{
    /// <summary>
    /// Ô chính: dòng DB nếu có, không thì giá trị mặc định trong appsettings (null nếu dịch vụ không có mặc định).
    /// Ô dự phòng: chỉ khi có dòng DB và đang bật, ngược lại null. Kết quả được cache 30 giây.
    /// </summary>
    Task<ResolvedConfig?> ResolveAsync(string serviceKey, SystemConfigSlot slot, CancellationToken ct);

    /// <summary>Xóa cache của cả hai ô để lần đọc kế tiếp thấy ngay giá trị mới (gọi sau khi lưu hoặc khôi phục).</summary>
    void Invalidate(string serviceKey);
}
