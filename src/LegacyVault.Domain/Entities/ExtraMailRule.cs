namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một thư bổ sung do Admin đặt: gửi cho Chủ kho vào một ngày tính từ hạn điểm danh của mỗi kỳ, ngoài các thư nhắc có sẵn.
/// DaysFromDue âm là trước hạn, dương là sau hạn (quá hạn).
/// </summary>
public class ExtraMailRule
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Tên để Admin nhận ra, không gửi đi.</summary>
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Số ngày tính từ hạn điểm danh: âm là trước hạn, dương là sau hạn.</summary>
    public int DaysFromDue { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Nội dung thân thư; mỗi dòng là một đoạn.</summary>
    public string Body { get; set; } = string.Empty;

    public string ButtonText { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public Guid? UpdatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
