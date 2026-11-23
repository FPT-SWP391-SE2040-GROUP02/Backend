using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class BeneficiaryMappings
{
    /// <summary>
    /// data là dữ liệu đã giải mã của chính hồ sơ này; assetIds là các tài sản đang được gán cho hồ sơ này
    /// (không có thì danh sách rỗng). Trả số CCCD đầy đủ.
    /// </summary>
    public static BeneficiaryResponse ToResponse(this Beneficiary beneficiary, BeneficiaryPersonalData data, List<Guid> assetIds)
    {
        return new BeneficiaryResponse
        {
            Id = beneficiary.Id,
            CitizenId = data.CitizenId,
            FullName = data.FullName,
            DateOfBirth = data.DateOfBirth,
            Email = data.Email,
            Phone = data.Phone,
            Message = data.Message,
            AssetIds = assetIds,
            CreatedAt = beneficiary.CreatedAt,
            UpdatedAt = beneficiary.UpdatedAt,
            Etag = Etag.From(beneficiary.UpdatedAt),
        };
    }
}
