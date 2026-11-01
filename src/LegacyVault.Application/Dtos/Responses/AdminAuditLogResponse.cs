namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Một dòng nhật ký toàn hệ thống cho Admin (ADMIN-06): thêm kho liên quan và tên, email người thực hiện để đọc được
/// mà không phải tra tài khoản. Không có dữ liệu nhạy cảm hơn dòng Owner xem (SEC-07).
/// </summary>
public class AdminAuditLogResponse : AuditLogResponse
{
    /// <summary>Kho liên quan; null với sự kiện không gắn kho.</summary>
    public Guid? VaultId { get; set; }

    /// <summary>Họ tên người thực hiện; null khi job nền thực hiện hoặc tài khoản không còn.</summary>
    public string? ActorFullName { get; set; }

    public string? ActorEmail { get; set; }
}
