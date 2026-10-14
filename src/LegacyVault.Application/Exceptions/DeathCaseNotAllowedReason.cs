namespace LegacyVault.Application.Exceptions;

/// <summary>Giá trị trường <c>reason</c> đi kèm DEATH_CASE_NOT_ALLOWED (trả dạng UPPER_SNAKE_CASE).</summary>
public enum DeathCaseNotAllowedReason
{
    /// <summary>DEATH-05: kế hoạch chưa chạy (nháp) hoặc đã sang giai đoạn khác; kèm trường planStatus.</summary>
    PlanNotRunning,
    /// <summary>DEATH-04: phiên bản đã được Executor xác nhận gửi, không sửa được nữa.</summary>
    VersionLocked,
    /// <summary>Hồ sơ đã duyệt, bị từ chối hoặc tạm dừng nên Executor không sửa/gửi được.</summary>
    CaseClosed,
    /// <summary>D5: chưa thể tạo phiên bản mới (đã có bản nháp, hoặc hồ sơ không ở trạng thái cần bổ sung).</summary>
    CaseNotRevisable,
}
