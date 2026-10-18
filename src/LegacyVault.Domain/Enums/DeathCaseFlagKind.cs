namespace LegacyVault.Domain.Enums;

/// <summary>Loại cờ cảnh báo gắn vào một phiên bản hồ sơ chứng tử để Verifier xem xét.</summary>
public enum DeathCaseFlagKind
{
    /// <summary>DEATH-10: Owner có điểm danh sau ngày mất được khai (tính theo giờ Việt Nam, D7). Chặn duyệt tới khi Verifier xem xét.</summary>
    CheckinAfterDeath = 0,
    /// <summary>Danh tính Owner khai trong hồ sơ không khớp. Chỉ khai báo, chưa có chức năng đặt cờ này.</summary>
    IdentityMismatch = 1,
}
