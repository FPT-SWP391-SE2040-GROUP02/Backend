using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Dựng response và lỗi dùng chung cho F2C (phía Owner và Verifier).</summary>
internal static class ObjectionMapper
{
    private const int CaseCodeLength = 8;

    public static ObjectionResponse ToResponse(DeathCaseObjection objection, DateTimeOffset now)
    {
        ObjectionResponse response = new ObjectionResponse();
        response.Id = objection.Id;
        response.Status = objection.Status.ToApiString();
        response.FirstReportedAt = objection.FirstReportedAt;
        response.LastReportedAt = objection.LastReportedAt;
        response.ReportCount = objection.ReportCount;
        response.SelfieDeadlineAt = objection.SelfieDeadlineAt;
        response.SelfieWindowOpen = ObjectionRules.IsSelfieWindowOpen(objection, now);
        response.SelfiePassed = objection.SelfiePassedAt != null;
        response.SelfiePassedAt = objection.SelfiePassedAt;
        response.SelfieAttemptsRemaining = ObjectionRules.MaxSelfieFailures - ObjectionRules.RecentFailures(objection, now);
        response.HasSelfieImage = objection.SelfieImageStorageKey != null;
        response.SelfieImageCapturedAt = objection.SelfieImageCapturedAt;
        response.SelfieImageMatched = objection.SelfieImageMatched;
        response.VerifierDueAt = objection.VerifierDueAt;
        response.InsufficientEvidenceCount = objection.InsufficientEvidenceCount;
        if (objection.Conclusion != null)
        {
            response.Conclusion = objection.Conclusion.Value.ToApiString();
        }
        response.ConclusionReason = objection.ConclusionReason;
        response.ConcludedAt = objection.ConcludedAt;
        response.ServerTime = now;
        return response;
    }

    public static string CaseCode(Guid caseId)
    {
        string hex = caseId.ToString("N");
        return hex.Substring(hex.Length - CaseCodeLength).ToUpperInvariant();
    }

    public static AppException CaseNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.DeathCaseNotFound, "Không tìm thấy hồ sơ chứng tử.");
    }

    public static AppException EditConflict()
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
            "Dữ liệu vừa được sửa ở nơi khác. Vui lòng tải lại rồi thử lại.");
    }

    public static AppException NotAllowed(DeathCaseNotAllowedReason reason)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason.ToApiString();
        return new AppException(AppErrorType.Conflict, ErrorCode.DeathCaseNotAllowed,
            "Hồ sơ chứng tử hiện không cho phép thao tác này.", extensions);
    }
}
