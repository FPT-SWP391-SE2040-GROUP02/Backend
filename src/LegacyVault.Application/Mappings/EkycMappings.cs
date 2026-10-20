using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Ekyc;
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
            Purpose = verification.Purpose.ToApiString(),
            IdentityMatch = verification.IdentityMatch.ToApiString(),
            BackSideChecked = verification.BackSideChecked,
            VerificationId = verification.Id,
            CitizenIdLast4 = verification.CitizenIdLast4,
            FaceMatchScore = verification.FaceMatchScore,
            MatchThreshold = EkycRules.MatchThreshold,
            SpoofCheckPassed = verification.SpoofCheckPassed,
            CreatedAt = verification.CreatedAt,
            CompletedAt = verification.CompletedAt,
        };
    }

    /// <summary>
    /// Phản hồi gửi mặt sau: trạng thái lượt như <see cref="ToResponse(EkycVerification)"/> kèm nơi cư trú đọc từ
    /// mặt sau. Địa chỉ chỉ đi qua phản hồi, không lưu vào lượt eKYC.
    /// </summary>
    public static EkycBackResponse ToBackResponse(this EkycVerification verification, string residenceAddress)
    {
        EkycBackResponse response = new EkycBackResponse();
        response.Status = verification.Status.ToApiString();
        response.Purpose = verification.Purpose.ToApiString();
        response.IdentityMatch = verification.IdentityMatch.ToApiString();
        response.BackSideChecked = verification.BackSideChecked;
        response.VerificationId = verification.Id;
        response.CitizenIdLast4 = verification.CitizenIdLast4;
        response.FaceMatchScore = verification.FaceMatchScore;
        response.MatchThreshold = EkycRules.MatchThreshold;
        response.SpoofCheckPassed = verification.SpoofCheckPassed;
        response.CreatedAt = verification.CreatedAt;
        response.CompletedAt = verification.CompletedAt;
        response.ResidenceAddress = residenceAddress;
        return response;
    }

    /// <summary>
    /// Tiến độ thử thách camera cho FE. Bỏ mã phiên và điểm số; thời hạn phiên chỉ có khi vừa tạo phiên (includeExpiry).
    /// </summary>
    public static EkycLivenessResponse ToResponse(this EkycLivenessState state, bool includeExpiry)
    {
        EkycLivenessResponse response = new EkycLivenessResponse();
        foreach (EkycLivenessAction action in state.Actions)
        {
            EkycLivenessActionResponse item = new EkycLivenessActionResponse();
            item.Key = action.Key;
            item.Label = action.Label;
            response.Actions.Add(item);
        }
        response.Completed = state.Completed;
        response.CurrentIndex = state.CurrentIndex;
        response.Prompt = state.Prompt;
        response.Calibrated = state.Calibrated;
        response.ChallengeCompleted = state.ChallengeCompleted;
        response.PadPassed = state.PadPassed;
        response.Passed = state.Passed;
        response.FailedReason = state.FailedReason;
        response.FrameHint = state.FrameHint;
        if (includeExpiry)
        {
            response.ExpiresInSeconds = state.ExpiresInSeconds;
        }
        return response;
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
