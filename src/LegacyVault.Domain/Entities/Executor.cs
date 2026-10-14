using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Executor do Owner mời cho một kho (bảng Executors).
/// </summary>
/// <remarks>
/// - Executor không phải vai trò hệ thống mà là quan hệ của một tài khoản với một kho.
///   Được mời không cho quyền đọc tài sản hay quyền quản trị.
/// - Mỗi dòng là một lời mời. UserId chỉ được gắn khi người được mời đăng nhập, đã xác minh email
///   và bấm chấp nhận; không gắn theo email. Mời lại sau khi hết hạn thì tạo dòng mới để giữ lịch sử.
/// - Chỉ lưu mã băm của token trong link mời; token gốc chỉ nằm trong email.
/// - Owner có thể đánh dấu "Executor này cũng là người nhận" và chọn hồ sơ người nhận tương ứng.
/// </remarks>
public class Executor
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    public Vault Vault { get; set; } = null!;

    /// <summary>Tài khoản đã chấp nhận lời mời; null khi chưa chấp nhận.</summary>
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Email Owner nhập khi mời, đã chuẩn hóa (bỏ khoảng trắng, chữ thường). Chỉ dùng để gửi lời mời.</summary>
    public string InviteeEmail { get; set; } = string.Empty;

    public ExecutorRole Role { get; set; }

    public ExecutorStatus Status { get; set; }

    /// <summary>Mã băm SHA-256 của token trong link mời, dạng hex. Không trùng nhau.</summary>
    public string InvitationTokenHash { get; set; } = string.Empty;

    /// <summary>Executor này cũng là người nhận trong kho.</summary>
    public bool IsBeneficiary { get; set; }

    /// <summary>Hồ sơ người nhận tương ứng khi IsBeneficiary là true; null khi không phải người nhận.</summary>
    public Guid? BeneficiaryId { get; set; }

    public Beneficiary? Beneficiary { get; set; }

    public DateTimeOffset InvitedAt { get; set; }

    /// <summary>Hạn của lời mời: 7 ngày kể từ lúc mời.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Lúc đã gửi email nhắc (sau 48 giờ chưa trả lời); mỗi lời mời chỉ nhắc một lần.</summary>
    public DateTimeOffset? ReminderSentAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>Lúc từ chối, hết hạn, bị thu hồi hoặc bị tạm dừng gần nhất.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Đổi sau mỗi lần sửa; dùng để phát hiện hai thao tác cùng sửa một lời mời.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
