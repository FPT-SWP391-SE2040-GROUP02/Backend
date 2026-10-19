using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Hồ sơ người nhận cấp kho: danh bạ người nhận, dùng lại cho nhiều gói (§5.2 bước 7; QUOTA-04, SETUP-17).
/// </summary>
/// <remarks>
/// Luật chung khi cài đặt:
/// - Dữ liệu cá nhân được mã hóa; response chỉ có 4 số cuối CCCD, không bao giờ có số đầy đủ (DATA-02).
/// - Không gửi thông báo, không tạo tài khoản cho người nhận (SETUP-02).
/// - Tạo hồ sơ không chiếm suất, không tăng ConfigVersion: suất chỉ tính khi hồ sơ được chỉ định vào gói (QUOTA-03).
/// - containerIds trong response là các gói đang chỉ định hồ sơ.
/// - Thao tác "thêm" (tạo, sửa) chặn khi kho CLOSED / kế hoạch bị khóa / dịch vụ đang ân hạn;
///   thao tác "giảm" (xóa) vẫn cho khi ân hạn (OPLAN-01).
/// Lỗi chung: 401 SESSION_EXPIRED, 404 VAULT_NOT_FOUND (không có kho / kho người khác),
/// 409 VAULT_NOT_EDITABLE (+reason: VAULT_CLOSED / PLAN_LOCKED / SERVICE_READ_ONLY).
/// </remarks>
public interface IBeneficiaryService
{
    /// <summary>
    /// Tạo hồ sơ. Đã có hồ sơ cùng CCCD, cùng họ tên (chuẩn hóa) và ngày sinh → trả hồ sơ cũ, Created = false.
    /// Lỗi: 403 PAID_TIER_REQUIRED (hạng Free, SETUP-17), 400 VALIDATION_ERROR,
    /// 409 BENEFICIARY_IDENTITY_CONFLICT (+beneficiaryId, QUOTA-04). Không kiểm tuổi; ngày sinh ở tương lai → 400.
    /// </summary>
    Task<CreateBeneficiaryResult> CreateBeneficiaryAsync(
        Guid vaultId, CreateBeneficiaryRequest request, CancellationToken ct);

    /// <summary>
    /// Một trang hồ sơ của kho. SortBy chỉ nhận "createdAt" (mặc định tăng dần), giá trị khác → 400.
    /// SearchTerm (1–4 chữ số) lọc theo đuôi của 4 số cuối CCCD; không tìm theo tên vì tên đã mã hóa.
    /// </summary>
    Task<PaginatedList<BeneficiaryResponse>> GetBeneficiariesAsync(
        Guid vaultId, PaginationRequest pagination, CancellationToken ct);

    /// <summary>Một hồ sơ. Lỗi: 404 BENEFICIARY_NOT_FOUND (cả khi hồ sơ thuộc kho khác).</summary>
    Task<BeneficiaryResponse> GetBeneficiaryAsync(Guid vaultId, Guid beneficiaryId, CancellationToken ct);

    /// <summary>
    /// Sửa họ tên/ngày sinh/liên hệ (request.Etag bắt buộc; không đổi được CCCD). Không có gì đổi → trả nguyên trạng.
    /// Hồ sơ đang ở gói mà có thay đổi thì tăng ConfigVersion (SETUP-04).
    /// Lỗi: 404 BENEFICIARY_NOT_FOUND, 409 EDIT_CONFLICT, 400 VALIDATION_ERROR.
    /// </summary>
    Task<BeneficiaryResponse> UpdateBeneficiaryAsync(
        Guid vaultId, Guid beneficiaryId, UpdateBeneficiaryRequest request, CancellationToken ct);

    /// <summary>
    /// Xóa hồ sơ (thao tác "giảm", vẫn cho khi ân hạn).
    /// Lỗi: 404 BENEFICIARY_NOT_FOUND, 409 BENEFICIARY_IN_USE (+containerIds, hồ sơ đang ở gói).
    /// </summary>
    Task DeleteBeneficiaryAsync(Guid vaultId, Guid beneficiaryId, CancellationToken ct);
}
