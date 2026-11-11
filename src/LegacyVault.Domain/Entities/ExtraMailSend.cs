namespace LegacyVault.Domain.Entities;

/// <summary>
/// Dấu một thư bổ sung đã được xử lý cho một kỳ điểm danh. Unique (RuleId, PeriodId) nên không thể gửi trùng dù job chạy lặp.
/// Sent = false là thư đã quá hạn gửi khi Admin tạo luật muộn, không gửi bù.
/// </summary>
public class ExtraMailSend
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid RuleId { get; set; }

    public Guid PeriodId { get; set; }

    /// <summary>true: đã gửi; false: bỏ qua, không gửi.</summary>
    public bool Sent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
