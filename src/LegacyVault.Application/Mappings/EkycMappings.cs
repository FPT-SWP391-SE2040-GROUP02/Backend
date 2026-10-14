using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class EkycMappings
{
    public static EkycStatusResponse ToResponse(this EkycVerification verification)
    {
        return new EkycStatusResponse
        {
            Status = verification.Status.ToApiString(),
            VerificationId = verification.Id,
            CitizenIdLast4 = verification.CitizenIdLast4,
            FaceMatchScore = verification.FaceMatchScore,
            MatchThreshold = EkycRules.MatchThreshold,
            SpoofCheckPassed = verification.SpoofCheckPassed,
            CreatedAt = verification.CreatedAt,
            CompletedAt = verification.CompletedAt,
        };
    }

    /// <summary>Người dùng chưa từng eKYC.</summary>
    public static EkycStatusResponse NotStarted()
    {
        return new EkycStatusResponse
        {
            Status = "NOT_STARTED",
            MatchThreshold = EkycRules.MatchThreshold,
        };
    }
}
