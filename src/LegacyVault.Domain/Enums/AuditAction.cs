namespace LegacyVault.Domain.Enums;

/// <summary>
/// Loại thao tác ghi vào nhật ký. Lưu dạng chữ nên chỉ thêm giá trị mới ở cuối, không đổi tên giá trị cũ.
/// </summary>
public enum AuditAction
{
    /// <summary>Owner bấm "Tạo kho của tôi" lần đầu.</summary>
    VaultCreated = 0,
    /// <summary>Owner đổi tên hoặc mô tả kho.</summary>
    VaultUpdated = 1,
    /// <summary>Owner tải lên một tài sản mới.</summary>
    AssetCreated = 2,
    /// <summary>Không còn dùng (đã bỏ gói tài sản); giữ giá trị để đọc nhật ký cũ.</summary>
    ContainerCreated = 3,
    /// <summary>Không còn dùng (đã bỏ gói tài sản); giữ giá trị để đọc nhật ký cũ.</summary>
    ContainerUpdated = 4,
    /// <summary>
    /// Owner xóa gói, tên cũ trước khi đổi Archived thành Removed. Chỉ còn trong nhật ký cũ; thao tác mới
    /// ghi ContainerRemoved. Giữ nguyên để không sửa nhật ký đã ghi.
    /// </summary>
    ContainerArchived = 5,
    /// <summary>Không còn dùng (đã bỏ gói tài sản); giữ giá trị để đọc nhật ký cũ.</summary>
    ContainerAssetAdded = 6,
    /// <summary>Không còn dùng (đã bỏ gói tài sản); giữ giá trị để đọc nhật ký cũ.</summary>
    ContainerAssetRemoved = 7,
    /// <summary>Không còn dùng (đã bỏ gói tài sản); giữ giá trị để đọc nhật ký cũ.</summary>
    ContainerBeneficiaryAdded = 8,
    /// <summary>Không còn dùng (đã bỏ gói tài sản); giữ giá trị để đọc nhật ký cũ.</summary>
    ContainerBeneficiaryRemoved = 9,
    /// <summary>Kế hoạch về DRAFT vì không còn tài sản nào được gán cho người nhận, Owner đã xác nhận.</summary>
    PlanDeactivated = 10,
    /// <summary>Owner tạo hồ sơ người nhận.</summary>
    BeneficiaryCreated = 11,
    /// <summary>Owner sửa hồ sơ người nhận.</summary>
    BeneficiaryUpdated = 12,
    /// <summary>Owner xóa hồ sơ người nhận không còn ở gói nào.</summary>
    BeneficiaryDeleted = 13,
    /// <summary>Không còn dùng (đã bỏ điểm danh); giữ giá trị để đọc nhật ký cũ.</summary>
    CheckedIn = 14,
    /// <summary>Không còn dùng (đã bỏ điểm danh); giữ giá trị để đọc nhật ký cũ.</summary>
    CheckInSettingsUpdated = 15,
    /// <summary>Không còn dùng (đã bỏ tạm giữ); giữ giá trị để đọc nhật ký cũ.</summary>
    HoldPlaced = 16,
    /// <summary>Không còn dùng (đã bỏ tạm giữ); giữ giá trị để đọc nhật ký cũ.</summary>
    HoldReleased = 17,
    /// <summary>Sự cố được giao lại người phụ trách.</summary>
    IncidentAssigned = 18,
    /// <summary>Sự cố được đóng.</summary>
    IncidentResolved = 19,
    /// <summary>Admin đổi vai trò hệ thống của tài khoản.</summary>
    UserRoleChanged = 20,
    /// <summary>Admin khóa, mở khóa hoặc vô hiệu hóa tài khoản.</summary>
    UserStatusChanged = 21,
    /// <summary>Admin giao lại Verifier cho hồ sơ chứng tử.</summary>
    VerifierReassigned = 22,
    /// <summary>Executor mở hồ sơ chứng tử.</summary>
    DeathCaseOpened = 25,
    /// <summary>Executor gửi một bản hồ sơ chứng tử, kể cả bản bổ sung.</summary>
    DeathCaseSubmitted = 26,
    /// <summary>Executor tạo bản bổ sung của hồ sơ chứng tử.</summary>
    DeathCaseVersionCreated = 27,
    /// <summary>Verifier xem xét cờ cảnh báo của hồ sơ.</summary>
    DeathCaseFlagReviewed = 28,
    /// <summary>Verifier yêu cầu Executor bổ sung hồ sơ.</summary>
    DeathCaseSupplementRequested = 29,
    /// <summary>Verifier từ chối hồ sơ chứng tử.</summary>
    DeathCaseRejected = 30,
    /// <summary>Verifier duyệt hồ sơ chứng tử và mở bàn giao.</summary>
    DeathCaseApproved = 31,
    /// <summary>Owner đổi tên hoặc mô tả tài sản.</summary>
    AssetUpdated = 32,
    /// <summary>Không còn dùng (đã bỏ phiên bản tài sản); giữ giá trị để đọc nhật ký cũ.</summary>
    AssetVersionCreated = 33,
    /// <summary>Owner xóa tài sản không còn ở gói nào.</summary>
    AssetDeleted = 34,
    /// <summary>Owner tải nội dung tài sản của chính mình.</summary>
    AssetContentDownloaded = 35,
    /// <summary>Admin tua thời gian của một kho thử bằng công cụ thử nghiệm (chỉ có ở Development).</summary>
    DevTimeTravel = 36,
    /// <summary>Admin đưa đồng hồ của kho thử về thời gian thực (chỉ có ở Development).</summary>
    DevClockReset = 37,
    /// <summary>Owner tạo đơn thanh toán.</summary>
    PaymentOrderCreated = 38,
    /// <summary>Owner hủy đơn thanh toán đang chờ.</summary>
    PaymentOrderCancelled = 39,
    /// <summary>Đơn thanh toán được thanh toán và quyền dịch vụ được cấp (job/webhook, không có người dùng).</summary>
    PaymentPaid = 40,
    /// <summary>Admin xóa dữ liệu eKYC của một tài khoản để làm lại từ đầu (công cụ thử nghiệm, chỉ có ở Development).</summary>
    DevEkycReset = 41,
    /// <summary>Admin đưa kho thử về trước bước eKYC và mua gói: xóa eKYC của Owner, trả quyền dịch vụ về Free, hủy đơn đang chờ (công cụ thử nghiệm, chỉ có ở Development).</summary>
    DevRewindBeforeEkyc = 42,
    /// <summary>Admin đổi ngưỡng so khớp khuôn mặt eKYC bằng công cụ thử nghiệm (chỉ có ở Development/Staging).</summary>
    DevUpdateEkycThreshold = 43,
    /// <summary>Admin bật hoặc tắt chế độ tạm ngừng eKYC bằng công cụ thử nghiệm (chỉ có ở Development/Staging).</summary>
    DevToggleEkycPause = 59,
    /// <summary>Admin tạo tài khoản hệ thống mới (Verifier hoặc Executor).</summary>
    AdminCreatedUser = 44,
    /// <summary>Owner đặt mã PIN của kho lần đầu.</summary>
    VaultPinSet = 45,
    /// <summary>Owner đổi mã PIN của kho bằng PIN cũ.</summary>
    VaultPinChanged = 46,
    /// <summary>Owner đặt lại mã PIN đã quên bằng mật khẩu tài khoản.</summary>
    VaultPinReset = 47,
    /// <summary>Nhập sai mã PIN của kho (kết quả Failed).</summary>
    VaultPinWrong = 48,
    /// <summary>Mã PIN của kho bị khóa tạm vì nhập sai nhiều lần (kết quả Denied).</summary>
    VaultPinLocked = 49,
    /// <summary>Không còn dùng (đã bỏ gói tài sản); giữ giá trị để đọc nhật ký cũ.</summary>
    ContainerRemoved = 50,
    /// <summary>Kho trả phí hết hạn dịch vụ, chưa có kỳ nối tiếp nên sang ân hạn chỉ đọc (do job, không có người dùng).</summary>
    ServiceGraceStarted = 51,
    /// <summary>Executor hủy hồ sơ chứng tử còn nháp (chưa gửi lần nào); hồ sơ và các trang đã tải bị xóa.</summary>
    DeathCaseDraftCancelled = 52,
    /// <summary>Admin cập nhật tham số cấu hình hệ thống.</summary>
    SystemSettingUpdated = 53,
    /// <summary>Admin lưu cấu hình một dịch vụ ngoài (model, địa chỉ, khóa...). Nhật ký chỉ ghi tên trường đã đổi, không ghi giá trị.</summary>
    SystemConfigUpdated = 54,
    /// <summary>Admin khôi phục cấu hình một dịch vụ ngoài về giá trị mặc định trong appsettings.</summary>
    SystemConfigReset = 55,
    /// <summary>Owner cấp mảnh khóa lần đầu cho Người thực thi.</summary>
    ExecutorKeyIssued = 56,
    /// <summary>Owner cấp lại mảnh khóa vì Người thực thi quên hoặc mất; mảnh cũ hết hiệu lực.</summary>
    ExecutorKeyReissued = 57,
    /// <summary>Admin giao hoặc đổi Người thực thi của một kho, kèm lý do (người được giao phải chấp nhận lời mời).</summary>
    ExecutorAssignedByAdmin = 58,
    /// <summary>Người xét duyệt duyệt hồ sơ người nhận (Chấp nhận hoặc Từ chối); lượt sang bước quyết định.</summary>
    HandoverSubmissionApproved = 60,
    /// <summary>Người xét duyệt yêu cầu người nhận bổ sung hồ sơ.</summary>
    HandoverSubmissionSupplementRequested = 61,
    /// <summary>Chủ tài sản ký di chúc điện tử bằng mã PIN của kho và kích hoạt kế hoạch bàn giao.</summary>
    PlanActivated = 62,
    /// <summary>Người thực thi tự chấp nhận lời mời làm Người thực thi của kho (ký xác nhận di chúc).</summary>
    ExecutorAccepted = 63,
    /// <summary>Chủ tài sản ký lại di chúc điện tử bằng mã PIN sau khi đổi cấu hình; tạo phiên bản di chúc mới.</summary>
    WillResigned = 64,
    /// <summary>Chủ tài sản xem danh tính đầy đủ (Chủ tài sản và người nhận) để lập di chúc. Nhật ký không ghi giá trị đã giải mã.</summary>
    WillIdentityViewed = 65,
    /// <summary>Đăng nhập thành công (mật khẩu, Google hoặc mã hai bước). Lý do chỉ ghi phương thức, không ghi dữ liệu nhạy cảm.</summary>
    LoginSucceeded = 66,
    /// <summary>Sai mật khẩu của tài khoản có thật (kết quả Failed). Email chưa đăng ký không ghi để nhật ký không bị tràn.</summary>
    LoginFailed = 67,
    /// <summary>Tài khoản bị khóa tạm vì đăng nhập sai nhiều lần (kết quả Denied).</summary>
    LoginLocked = 68,
    /// <summary>Admin bật xác thực hai bước.</summary>
    TwoFactorEnabled = 69,
    /// <summary>Admin tắt xác thực hai bước.</summary>
    TwoFactorDisabled = 70,
    /// <summary>Người dùng đặt lại mật khẩu bằng liên kết gửi qua email.</summary>
    PasswordResetCompleted = 71,
    /// <summary>Người dùng đổi mật khẩu khi đang đăng nhập.</summary>
    PasswordChanged = 72,
    /// <summary>Lượt xác minh eKYC đạt. Nhật ký không ghi điểm số hay dữ liệu giấy tờ.</summary>
    EkycPassed = 73,
    /// <summary>Lượt xác minh eKYC không đạt (kết quả Failed).</summary>
    EkycFailed = 74,
    /// <summary>Owner gán một tài sản cho một người nhận (đích là tài sản; lý do ghi id người nhận).</summary>
    AssetAssigned = 75,
    /// <summary>Owner gỡ gán một tài sản khỏi một người nhận (đích là tài sản; lý do ghi id người nhận).</summary>
    AssetUnassigned = 76,
    /// <summary>Luật sư nhắc Người thực thi ký biên bản bàn giao đã chốt (đích là kho; không ghi nội dung thư).</summary>
    HandoverExecutorReminded = 77,
    /// <summary>Chủ tài sản ký biên bản phân chia trước, chờ Người thực thi xem và ký (kế hoạch chưa có hiệu lực).</summary>
    WillSigned = 78,
    /// <summary>Người thực thi xem và đồng ý phiên bản biên bản phân chia Chủ tài sản ký lại.</summary>
    ExecutorWillConfirmed = 79,
    /// <summary>Người thực thi không đồng ý phiên bản biên bản phân chia Chủ tài sản ký lại.</summary>
    ExecutorWillDeclined = 80,
    /// <summary>Người thực thi hoặc người được mời mở biên bản phân chia đầy đủ. Nhật ký không ghi danh tính hay tên tài sản.</summary>
    ExecutorWillViewed = 81,
    /// <summary>Admin sửa họ tên, số điện thoại, số chứng chỉ hoặc số thẻ luật sư của một tài khoản nhân sự (đích là tài khoản; không ghi giá trị).</summary>
    AdminUpdatedUserProfile = 82,
}
