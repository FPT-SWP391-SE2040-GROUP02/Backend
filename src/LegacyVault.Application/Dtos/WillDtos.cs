using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos;

/// <summary>Chủ kho ký lại di chúc; configVersion là bản cấu hình đang xem, chống ký đè lên thay đổi mới hơn.</summary>
public sealed class SignWillRequest
{
    [Range(1, int.MaxValue)]
    public int ConfigVersion { get; set; }
}

/// <summary>
/// Số lượng thay đổi so với bản ký gần nhất (chỉ số đếm, không có tên). Tài sản và người nhận chỉ đếm trong các gói
/// có ở cả bản ký lẫn cấu hình hiện tại; changedRecipientCount là số gói như vậy mà tập người nhận khác bản ký.
/// </summary>
public sealed record UnsignedWillChangesResponse(
    int RemovedContainers, int AddedContainers, int RemovedAssets, int AddedAssets,
    int RemovedRecipients, int AddedRecipients, int ChangedRecipientCount);

/// <summary>Danh tính đầy đủ của Chủ kho (đã giải mã) để lập di chúc.</summary>
public sealed record WillOwnerIdentityResponse(string FullName, DateOnly DateOfBirth, string CitizenId, string ResidenceAddress);

/// <summary>Danh tính đầy đủ của một người nhận (đã giải mã) để lập di chúc.</summary>
public sealed record WillRecipientIdentityResponse(Guid BeneficiaryId, string FullName, DateOnly DateOfBirth, string CitizenId);

/// <summary>Owner null khi Chủ kho chưa có danh tính eKYC đã gắn.</summary>
public sealed record WillIdentityResponse(WillOwnerIdentityResponse? Owner, List<WillRecipientIdentityResponse> Recipients);
