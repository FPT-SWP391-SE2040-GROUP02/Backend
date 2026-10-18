namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Mã lỗi gửi cho FE (trường <c>code</c> của ProblemDetails).
/// Khi trả JSON, tên enum được đổi sang UPPER_SNAKE_CASE (EnumText): VaultNotFound → "VAULT_NOT_FOUND".
/// </summary>
/// <remarks>
/// FE so sánh theo chuỗi, nên ĐỔI TÊN một giá trị là đổi contract. Thêm mã mới thì cập nhật bảng mã lỗi
/// trong docs/api-contract.md.
/// </remarks>
public enum ErrorCode
{
    ValidationError,
    SessionExpired,
    AccountNotActive,
    EmailNotVerified,
    VaultNotFound,
    VaultNotEditable,
    EditConflict,
    AssetNotFound,
    HandoverInvitationInvalid,
    HandoverNotFound,
    HandoverNotAllowed,
    HandoverAlreadyClaimed,
    HandoverDecisionExpired,
    GrantNotActive,
    ContentIntegrityFailed,
    StorageQuotaExceeded,
    FileTooLarge,
    InternalError,
    ContainerNotFound,
    ContainerLimitExceeded,
    ContainerArchived,
    PlanDeactivationConfirmationRequired,
    PaidTierRequired,
    AssetAlreadyInContainer,
    BeneficiaryNotFound,
    BeneficiaryIdentityConflict,
    MinorConfirmationRequired,
    BeneficiaryInUse,
    BeneficiarySlotsExceeded,
    CheckinNotAllowed,
    EkycImageRejected,
    EkycDocumentExpired,
    EkycServiceUnavailable,
    DeathCaseNotFound,
    DeathCaseNotAllowed,
    DeathCaseIncomplete,
    DeathCaseFileLimitExceeded,
    RoleNotAllowed,
    StorageUnavailable,
    ExecutorInvitationInvalid,
    ExecutorNotFound,
    IncidentNotFound,
    IncidentNotOpen,
    HoldNotFound,
    AdminActionNotAllowed,
    UserNotFound,
    ExecutorInvitationConflict,
    ExecutorNotAllowed,
    PlanActivationNotAllowed,
    PlanActivationRequirementsNotMet,
    PolicyAcknowledgementRequired,
    EkycConsentRequired,
    EkycDocumentMismatch,
    EkycLivenessRequired,
    EkycLivenessNotPassed,
    InvalidResetToken,
    PaymentNotAllowed,
}

/// <summary>Giá trị trường <c>reason</c> đi kèm VAULT_NOT_EDITABLE (cũng trả dạng UPPER_SNAKE_CASE).</summary>
public enum VaultNotEditableReason
{
    /// <summary>Kho đã đóng (SETUP-18).</summary>
    VaultClosed,
    /// <summary>Kế hoạch đã ghim snapshot/đang bàn giao/đã đóng: chặn sửa cấu hình (SETUP-05).</summary>
    PlanLocked,
    /// <summary>Dịch vụ đang ân hạn/chờ rà soát: chặn thao tác thêm (OPLAN-01).</summary>
    ServiceReadOnly,
}
