using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class BeneficiaryMappings
{
    /// <summary>
    /// data là dữ liệu đã giải mã của chính hồ sơ này; today là ngày hiện tại ở Việt Nam để tính IsMinor.
    /// Không đưa CitizenId đầy đủ vào response (DATA-02).
    /// </summary>
    public static BeneficiaryResponse ToResponse(this Beneficiary beneficiary, BeneficiaryPersonalData data, DateOnly today)
    {
        return new BeneficiaryResponse
        {
            Id = beneficiary.Id,
            CitizenIdLast4 = beneficiary.CitizenIdLast4,
            FullName = data.FullName,
            DateOfBirth = data.DateOfBirth,
            Email = data.Email,
            Phone = data.Phone,
            IsMinor = BeneficiaryRules.IsMinor(data.DateOfBirth, today),
            MinorAcknowledgedAt = beneficiary.MinorAcknowledgedAt,
            // Chưa có module chỉ định (nhánh feature/designations).
            ContainerIds = new List<Guid>(),
            CreatedAt = beneficiary.CreatedAt,
            UpdatedAt = beneficiary.UpdatedAt,
            Etag = Etag.From(beneficiary.UpdatedAt),
        };
    }
}
