using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Sự cố cần người xử lý (bảng Incidents). Việc chờ luôn có người phụ trách, hạn và ngày xem lại (ASSIGN-04, AC-40):
/// không có ai nhận được thì AssigneeUserId = null kèm NextReviewAt, không bao giờ để trôi.
/// </summary>
/// <remarks>
/// - Mỗi (Type, TargetType, TargetId) chỉ một sự cố đang mở (chỉ mục unique có lọc): job chạy lặp không tạo trùng.
/// - Summary là câu mô tả chung, KHÔNG chứa CCCD, email, token hay tên tài sản (SEC-07).
/// - Admin không dùng sự cố để mở tài sản hay tự duyệt thay (ASSIGN-05).
/// </remarks>
public class Incident
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public IncidentType Type { get; set; }

    public IncidentStatus Status { get; set; }

    public Guid? VaultId { get; set; }

    /// <summary>Loại đối tượng liên quan dạng chữ ngắn (DEATH_CASE, PLAN, HOLD, TURN...); chuỗi rỗng khi không có.</summary>
    public string TargetType { get; set; } = string.Empty;

    public Guid? TargetId { get; set; }

    public string Summary { get; set; } = string.Empty;

    public Guid? AssigneeUserId { get; set; }

    /// <summary>Hạn xử lý.</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>Ngày xem lại tiếp theo (tối đa 7 ngày một lần).</summary>
    public DateTimeOffset NextReviewAt { get; set; }

    /// <summary>Số lần đã nhắc người phụ trách (nhắc sau 48 giờ), 0 chưa nhắc.</summary>
    public int RemindedCount { get; set; }

    /// <summary>Lúc đã báo Admin vì quá 72 giờ chưa xử lý; null khi chưa.</summary>
    public DateTimeOffset? EscalatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public Guid? ResolvedByUserId { get; set; }

    public string? ResolutionNote { get; set; }

    /// <summary>Concurrency token: hai người cùng nhận hoặc đóng một sự cố chỉ một bên thắng.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<IncidentEvent> Events { get; set; } = new List<IncidentEvent>();
}

/// <summary>Lịch sử xử lý sự cố, chỉ thêm, không sửa (bảng IncidentEvents).</summary>
public class IncidentEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid IncidentId { get; set; }

    public IncidentEventKind Kind { get; set; }

    public Guid? ActorUserId { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset At { get; set; }
}
