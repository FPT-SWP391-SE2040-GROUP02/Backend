namespace LegacyVault.Domain.Enums;

/// <summary>Trạng thái báo "Tôi còn sống" (F2C).</summary>
public enum ObjectionStatus
{
    /// <summary>Đang chặn: hồ sơ chứng tử tạm dừng, chờ Verifier kết luận.</summary>
    Open = 0,
    /// <summary>Verifier đã kết luận (Conclusion khác InsufficientEvidence).</summary>
    Concluded = 1,
}
