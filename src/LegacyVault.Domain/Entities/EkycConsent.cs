using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Việc người dùng đồng ý cho hệ thống chụp và xử lý giấy tờ, khuôn mặt (bảng EkycConsents), SRS EKYC-03.
/// </summary>
/// <remarks>
/// - Ghi lại mục đích, phiên bản thông báo và thời điểm đồng ý TRƯỚC khi chụp. Không lưu ảnh hay dữ liệu định danh.
/// - Mỗi lần đồng ý là một dòng mới; dòng mới nhất của (người dùng, mục đích) là dòng được xét.
/// </remarks>
public class EkycConsent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public EkycPurpose Purpose { get; set; }

    /// <summary>Phiên bản thông báo xin đồng ý mà người dùng đã thấy (EkycRules.NoticeVersion lúc đó).</summary>
    public string NoticeVersion { get; set; } = string.Empty;

    public DateTimeOffset ConsentedAt { get; set; }
}
