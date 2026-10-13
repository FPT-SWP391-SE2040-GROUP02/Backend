using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Tài khoản Google (hoặc nhà cung cấp khác) đã liên kết với User (bảng ExternalLogins), AUTH-01..03.
/// </summary>
/// <remarks>
/// - AUTH-01: nhận diện bằng <c>sub</c> ổn định của Google, KHÔNG bằng email (email Google có thể đổi).
/// - Ràng buộc DB cần có: unique (Provider, ProviderSubject) và unique (UserId, Provider).
/// </remarks>
public class ExternalLogin
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public LoginProvider Provider { get; set; }

    /// <summary>Claim <c>sub</c> của Google.</summary>
    public string ProviderSubject { get; set; } = string.Empty;

    /// <summary>Email Google lúc liên kết, chỉ để hiển thị; không dùng để tìm tài khoản (AUTH-02).</summary>
    public string? ProviderEmail { get; set; }

    public DateTimeOffset LinkedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
