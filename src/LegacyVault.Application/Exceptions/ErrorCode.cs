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
    EmailAlreadyExists,
    EmailNotFound,
    SessionExpired,
    AccountNotActive,
    EmailNotVerified,
    VaultNotFound,
    VaultNotEditable,
    EditConflict,
    AssetNotFound,
    /// <summary>404: tài khoản chưa có ảnh đại diện.</summary>
    AvatarNotFound,
    /// <summary>404: chưa có ảnh CCCD (Owner xác minh trước khi lưu ảnh) hoặc chưa có ảnh selfie báo còn sống.</summary>
    IdentityImageNotFound,
    /// <summary>409: tài sản còn trong gói hoặc đã được ghim vào snapshot bàn giao nên không xóa được.</summary>
    AssetInUse,
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
    // Không còn dùng (bỏ SETUP-19). Giữ nguyên vị trí trong enum để không đổi giá trị các mã phía sau.
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
    EkycIdentityMismatch,
    EkycAlreadyVerified,
    PaymentNotAllowed,
    PaymentOrderPending,
    PaymentOrderNotPayable,
    PaymentOrderNotFound,
    WebhookUnauthorized,
    /// <summary>Công cụ thử nghiệm: mốc thời gian không áp dụng được cho trạng thái hiện tại của kho.</summary>
    DevToolNotAllowed,
    /// <summary>409: kho đã có tài sản nhưng chưa đặt mã PIN.</summary>
    VaultPinNotSet,
    /// <summary>409: kho đã có mã PIN, không đặt lần đầu nữa.</summary>
    VaultPinAlreadySet,
    /// <summary>403: thiếu header X-Vault-Pin.</summary>
    VaultPinRequired,
    /// <summary>403: sai mã PIN, kèm attemptsRemaining.</summary>
    InvalidVaultPin,
    /// <summary>423: PIN đang bị khóa tạm, kèm lockedUntil.</summary>
    VaultPinLocked,
    /// <summary>400: PIN quá dễ đoán (số giống nhau, dãy tăng/giảm).</summary>
    VaultPinTooWeak,
    /// <summary>409: tài khoản không có mật khẩu (đăng nhập bằng Google) nên chưa đặt lại PIN được.</summary>
    VaultPinResetNeedsPassword,
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
