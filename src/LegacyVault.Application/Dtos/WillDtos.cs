using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos;

/// <summary>Chủ tài sản ký lại di chúc; configVersion là bản cấu hình đang xem, chống ký đè lên thay đổi mới hơn.</summary>
public sealed class SignWillRequest
{
    [Range(1, int.MaxValue)]
    public int ConfigVersion { get; set; }
}

/// <summary>
/// Số lượng thay đổi so với bản ký gần nhất (chỉ số đếm, không có tên): số cặp tài sản - người nhận mới thêm,
/// số cặp đã gỡ, và số người nhận bị ảnh hưởng bởi hai loại thay đổi đó.
/// </summary>
public sealed record UnsignedWillChangesResponse(int AddedAssignments, int RemovedAssignments, int AffectedRecipientCount);

/// <summary>Danh tính đầy đủ của Chủ tài sản (đã giải mã) để lập di chúc.</summary>
public sealed record WillOwnerIdentityResponse(string FullName, DateOnly DateOfBirth, string CitizenId, string ResidenceAddress);

/// <summary>Danh tính đầy đủ của một người nhận (đã giải mã) để lập di chúc.</summary>
public sealed record WillRecipientIdentityResponse(Guid BeneficiaryId, string FullName, DateOnly DateOfBirth, string CitizenId);

/// <summary>Owner null khi Chủ tài sản chưa có danh tính eKYC đã gắn.</summary>
public sealed record WillIdentityResponse(WillOwnerIdentityResponse? Owner, List<WillRecipientIdentityResponse> Recipients);

/// <summary>
/// Biên bản phân chia đầy đủ cho Người thực thi (và người đang được mời) đọc trước khi ký: bản Chủ tài sản ký gần nhất, dùng
/// đúng kiểu người nhận của plan-setup và danh tính của will/identity. Không có nội dung tài sản, lời nhắn (HasMessage luôn
/// false) hay liên hệ của người nhận.
/// </summary>
public sealed record ExecutorWillResponse(int VersionNo, DateTimeOffset SignedAt, string OwnerName, string OwnerEmail,
    List<PlanSetupRecipientResponse> Recipients, int UnassignedAssetCount, WillIdentityResponse Identity);
