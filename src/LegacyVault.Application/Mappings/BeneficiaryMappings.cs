using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class BeneficiaryMappings
{
    /// <summary>
    /// data là dữ liệu đã giải mã của chính hồ sơ này; containerIds là các gói đang chỉ định hồ sơ này
    /// (không có thì danh sách rỗng). Trả số CCCD đầy đủ.
    /// </summary>
    public static BeneficiaryResponse ToResponse(this Beneficiary beneficiary, BeneficiaryPersonalData data, List<Guid> containerIds)
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
            ContainerIds = containerIds,
            CreatedAt = beneficiary.CreatedAt,
            UpdatedAt = beneficiary.UpdatedAt,
            Etag = Etag.From(beneficiary.UpdatedAt),
        };
    }
}
