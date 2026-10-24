namespace LegacyVault.Domain.Enums;

/// <summary>
/// Loại thao tác ghi vào nhật ký (SEC-07, SEC-13). Lưu dạng chữ nên chỉ thêm giá trị mới ở cuối, không đổi tên giá trị cũ.
/// </summary>
public enum AuditAction
{
    /// <summary>Owner bấm "Tạo kho của tôi" lần đầu (SETUP-12).</summary>
    VaultCreated = 0,
    /// <summary>Owner đổi tên hoặc mô tả kho.</summary>
    VaultUpdated = 1,
    /// <summary>Owner tải lên một tài sản mới (ASSET-01).</summary>
    AssetCreated = 2,
    /// <summary>Owner tạo gói (SETUP-04).</summary>
    ContainerCreated = 3,
    /// <summary>Owner đổi tên hoặc mô tả gói (SETUP-04).</summary>
    ContainerUpdated = 4,
    /// <summary>Owner lưu trữ gói (SETUP-16).</summary>
    ContainerArchived = 5,
    /// <summary>Owner thêm tài sản vào gói (SETUP-04).</summary>
    ContainerAssetAdded = 6,
    /// <summary>Owner gỡ tài sản khỏi gói (SETUP-04).</summary>
    ContainerAssetRemoved = 7,
    /// <summary>Owner chỉ định người nhận cho gói (SETUP-04).</summary>
    ContainerBeneficiaryAdded = 8,
    /// <summary>Owner gỡ người nhận khỏi gói (SETUP-04).</summary>
    ContainerBeneficiaryRemoved = 9,
    /// <summary>Kế hoạch về DRAFT vì mất gói READY cuối cùng, Owner đã xác nhận (SETUP-16).</summary>
    PlanDeactivated = 10,
    /// <summary>Owner tạo hồ sơ người nhận (QUOTA-04).</summary>
    BeneficiaryCreated = 11,
    /// <summary>Owner sửa hồ sơ người nhận.</summary>
    BeneficiaryUpdated = 12,
    /// <summary>Owner xóa hồ sơ người nhận không còn ở gói nào.</summary>
    BeneficiaryDeleted = 13,
    /// <summary>Owner điểm danh (F2A).</summary>
    CheckedIn = 14,
    /// <summary>Owner đổi chu kỳ hoặc thời gian chờ điểm danh (DMS-01).</summary>
    CheckInSettingsUpdated = 15,
    /// <summary>Admin hoặc job đặt hold.</summary>
    HoldPlaced = 16,
    /// <summary>Hold được gỡ.</summary>
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
    /// <summary>Owner báo còn sống (F2C).</summary>
    ObjectionReported = 23,
    /// <summary>Verifier kết luận báo còn sống.</summary>
    ObjectionConcluded = 24,
    /// <summary>Executor mở hồ sơ chứng tử (DEATH-05).</summary>
    DeathCaseOpened = 25,
    /// <summary>Executor gửi một bản hồ sơ chứng tử, kể cả bản bổ sung.</summary>
    DeathCaseSubmitted = 26,
    /// <summary>Executor tạo bản bổ sung của hồ sơ chứng tử.</summary>
    DeathCaseVersionCreated = 27,
    /// <summary>Verifier xem xét cờ cảnh báo của hồ sơ (DEATH-10).</summary>
    DeathCaseFlagReviewed = 28,
    /// <summary>Verifier yêu cầu Executor bổ sung hồ sơ.</summary>
    DeathCaseSupplementRequested = 29,
    /// <summary>Verifier từ chối hồ sơ chứng tử (DEATH-12).</summary>
    DeathCaseRejected = 30,
    /// <summary>Verifier duyệt hồ sơ chứng tử và mở bàn giao (DEATH-06).</summary>
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
    /// <summary>Owner tạo đơn thanh toán (SRS §8.3).</summary>
    PaymentOrderCreated = 38,
    /// <summary>Owner hủy đơn thanh toán đang chờ.</summary>
    PaymentOrderCancelled = 39,
    /// <summary>Đơn thanh toán được thanh toán và quyền dịch vụ được cấp (job/webhook, không có người dùng).</summary>
    PaymentPaid = 40,
    /// <summary>Admin xóa dữ liệu eKYC của một tài khoản để làm lại từ đầu (công cụ thử nghiệm, chỉ có ở Development).</summary>
    DevEkycReset = 41,
}
