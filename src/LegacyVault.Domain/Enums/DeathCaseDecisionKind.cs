namespace LegacyVault.Domain.Enums;

/// <summary>
/// Loại quyết định của Verifier trên một hồ sơ chứng tử (bảng DeathCaseDecisions), SRS §6.2 F2B.
/// Lưu dạng chữ; không đổi tên giá trị đã có.
/// </summary>
public enum DeathCaseDecisionKind
{
    /// <summary>DEATH-01: yêu cầu Executor bổ sung; một hồ sơ có thể có nhiều dòng loại này.</summary>
    SupplementRequested = 0,
    /// <summary>DEATH-12: từ chối, hồ sơ đóng, kế hoạch về trạng thái trước đó. Quyết định cuối.</summary>
    Rejected = 1,
    /// <summary>DEATH-06: duyệt, mở bàn giao F3. Quyết định cuối.</summary>
    Approved = 2,
}
