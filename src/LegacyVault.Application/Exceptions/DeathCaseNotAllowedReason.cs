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
    /// <summary>Duyệt khi hồ sơ không ở UnderReview; bổ sung, từ chối, xem xét cờ khi không ở UnderReview/SupplementRequired.</summary>
    NotUnderReview,
    /// <summary>DEATH-04: Executor đang có bản nháp mới hơn bản đang xét, chưa duyệt được.</summary>
    NewerVersionPending,
    /// <summary>DEATH-08: chưa báo được Owner nên đồng hồ 72 giờ chưa chạy.</summary>
    OwnerNotNotified,
    /// <summary>DEATH-09: chưa hết 72 giờ phản đối của Owner.</summary>
    ObjectionPeriodNotOver,
    /// <summary>DEATH-10: còn cờ của bản đang xét chưa được Verifier xem xét.</summary>
    FlagsNotReviewed,
    /// <summary>ROLE-03, AC-67: Verifier là Owner, Executor đang hiệu lực hoặc người gửi bản đang xét.</summary>
    VerifierConflict,
    /// <summary>Kế hoạch không ở DeathCaseOpen, thiếu StatusBeforeCase, hoặc đã có lượt bàn giao.</summary>
    PlanStateInvalid,

    /// <summary>F2C: báo còn sống trước đó đã bị bác, muốn mở lại phải có bằng chứng mới (ALIVE-09).</summary>
    ObjectionAlreadyRejected,

    /// <summary>F2C: đã quá 7 ngày kể từ lần báo đầu, không chụp selfie nữa (ALIVE-03).</summary>
    ObjectionWindowClosed,

    /// <summary>F2C: đã thất bại 3 lần trong 24 giờ (EKYC-20).</summary>
    SelfieAttemptsExceeded,

    /// <summary>F2C: báo không còn mở để kết luận.</summary>
    ObjectionNotOpen,

    /// <summary>F2C: kết luận còn sống nhưng chưa có selfie đạt và chưa hết hạn 7 ngày (ALIVE-02, ALIVE-04).</summary>
    AliveEvidenceRequired,

    /// <summary>Kho đang có hold (Admin hoặc báo còn sống) nên chưa duyệt được.</summary>
    HoldActive,
}
