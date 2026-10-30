using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Container (gói bàn giao) trong kho của người đang đăng nhập (§5.2 bước 2 và 6;
/// SETUP-03, SETUP-04, SETUP-16, SETUP-17; NFR-14).
/// </summary>
/// <remarks>
/// Luật chung khi cài đặt:
/// - Mọi thay đổi gói (tạo, đổi tên, xóa gói, thêm/gỡ tài sản, thêm/gỡ người nhận) tăng HandoverPlan.ConfigVersion (SETUP-04).
/// - Status DRAFT/READY do backend tự tính lại sau mỗi thay đổi; READY cần ≥1 tài sản VÀ ≥1 chỉ định người nhận (SETUP-16).
/// - Thao tác "thêm" (tạo, đổi tên, thêm tài sản/người nhận) chặn khi kho CLOSED / có hồ sơ chứng tử / dịch vụ đang ân hạn;
///   thao tác "giảm" (xóa gói, gỡ tài sản/người nhận) vẫn cho khi ân hạn (OPLAN-01).
/// Lỗi chung: 401 SESSION_EXPIRED, 404 VAULT_NOT_FOUND (không có kho / kho người khác),
/// 409 VAULT_NOT_EDITABLE (+reason: VAULT_CLOSED / PLAN_LOCKED / SERVICE_READ_ONLY).
/// </remarks>
public interface IContainerService
{
    /// <summary>
    /// Tạo gói rỗng ở DRAFT. Hạng Free cũng được tạo (SETUP-17).
    /// Lỗi: 409 CONTAINER_LIMIT_EXCEEDED (+maxContainers, đủ 50 gói chưa bị xóa), 400 VALIDATION_ERROR.
    /// </summary>
    Task<ContainerResponse> CreateContainerAsync(Guid vaultId, CreateContainerRequest request, CancellationToken ct);

    /// <summary>
    /// Một trang gói của kho. includeRemoved = false thì bỏ gói đã xóa.
    /// SearchTerm lọc theo tên; SortBy cho phép "name", "createdAt" (mặc định createdAt tăng dần), giá trị khác → 400.
    /// </summary>
    Task<PaginatedList<ContainerResponse>> GetContainersAsync(
        Guid vaultId, bool includeRemoved, PaginationRequest pagination, CancellationToken ct);

    /// <summary>Một gói. Lỗi: 404 CONTAINER_NOT_FOUND (cả khi gói thuộc kho khác).</summary>
    Task<ContainerResponse> GetContainerAsync(Guid vaultId, Guid containerId, CancellationToken ct);

    /// <summary>
    /// Đổi tên/mô tả (request.Etag bắt buộc). Lỗi: 409 EDIT_CONFLICT,
    /// 404 CONTAINER_NOT_FOUND, 409 CONTAINER_REMOVED, 400 VALIDATION_ERROR.
    /// </summary>
    Task<ContainerResponse> UpdateContainerAsync(
        Guid vaultId, Guid containerId, UpdateContainerRequest request, CancellationToken ct);

    /// <summary>
    /// Xóa gói: gỡ hết tài sản và chỉ định người nhận, chuyển REMOVED. Gói đã xóa → trả nguyên trạng.
    /// Lỗi: 404 CONTAINER_NOT_FOUND, 409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED (SETUP-16).
    /// </summary>
    Task<ContainerResponse> RemoveContainerAsync(
        Guid vaultId, Guid containerId, ConfirmPlanImpactRequest? request, CancellationToken ct);

    /// <summary>
    /// Đưa một tài sản CỦA KHO NÀY vào gói, rồi tính lại DRAFT/READY. Đã ở đúng gói này → trả nguyên trạng.
    /// Cần module Tài sản để kiểm tra assetId.
    /// Lỗi: 404 CONTAINER_NOT_FOUND, 404 ASSET_NOT_FOUND, 409 CONTAINER_REMOVED,
    /// 403 PAID_TIER_REQUIRED (hạng Free, SETUP-17 — chặn ở API, không chỉ ẩn nút),
    /// 409 ASSET_ALREADY_IN_CONTAINER (+containerId, tài sản đang ở gói khác).
    /// </summary>
    Task<ContainerResponse> AddAssetAsync(
        Guid vaultId, Guid containerId, AddContainerAssetRequest request, CancellationToken ct);

    /// <summary>
    /// Gỡ tài sản khỏi gói (KHÔNG xóa tài sản, KHÔNG giải phóng dung lượng — QUOTA-02), rồi tính lại DRAFT/READY.
    /// Tài sản không ở trong gói → trả nguyên trạng.
    /// Lỗi: 404 CONTAINER_NOT_FOUND, 409 CONTAINER_REMOVED, 409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED.
    /// </summary>
    Task<ContainerResponse> RemoveAssetAsync(
        Guid vaultId, Guid containerId, Guid assetId, ConfirmPlanImpactRequest? request, CancellationToken ct);

    /// <summary>
    /// Chỉ định một hồ sơ người nhận CỦA KHO NÀY cho gói, rồi tính lại DRAFT/READY. Đã ở đúng gói này → trả nguyên trạng.
    /// Người chưa ở gói nào của kho chiếm thêm một suất (QUOTA-03).
    /// Lỗi: 404 CONTAINER_NOT_FOUND, 404 BENEFICIARY_NOT_FOUND, 409 CONTAINER_REMOVED,
    /// 403 PAID_TIER_REQUIRED (hạng Free, SETUP-17), 409 BENEFICIARY_SLOTS_EXCEEDED (+usedSlots, maxSlots; QUOTA-05).
    /// </summary>
    Task<ContainerResponse> AddBeneficiaryAsync(
        Guid vaultId, Guid containerId, AddContainerBeneficiaryRequest request, CancellationToken ct);

    /// <summary>
    /// Gỡ người nhận khỏi gói (KHÔNG xóa hồ sơ), rồi tính lại DRAFT/READY. Người nhận không ở trong gói → trả nguyên trạng.
    /// Người còn ở gói khác vẫn chiếm suất (AC-103).
    /// Lỗi: 404 CONTAINER_NOT_FOUND, 409 CONTAINER_REMOVED, 409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED.
    /// </summary>
    Task<ContainerResponse> RemoveBeneficiaryAsync(
        Guid vaultId, Guid containerId, Guid beneficiaryId, ConfirmPlanImpactRequest? request, CancellationToken ct);
}
