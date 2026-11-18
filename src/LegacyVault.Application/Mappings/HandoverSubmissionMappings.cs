using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Mappings;

/// <summary>Đổi hồ sơ người nhận sang dữ liệu cho màn Người xét duyệt. Không có ảnh căn cước hay số căn cước đầy đủ.</summary>
public static class HandoverSubmissionMappings
{
    /// <summary>Một dòng trong danh sách hồ sơ chờ kiểm tra.</summary>
    public static VerifierHandoverSubmissionSummaryResponse ToVerifierSummary(this HandoverSubmission submission)
    {
        VerifierHandoverSubmissionSummaryResponse response = new VerifierHandoverSubmissionSummaryResponse();
        FillSummary(response, submission);
        return response;
    }

    /// <summary>Chi tiết hồ sơ thuộc một biên bản: duyệt được khi biên bản đang chờ Người xét duyệt, không còn chặn theo kết quả đối chiếu tự động.</summary>
    public static VerifierHandoverSubmissionResponse ToVerifierDetail(this HandoverSubmission submission, HandoverDocument document)
    {
        VerifierHandoverSubmissionResponse response = submission.ToVerifierDetail();
        response.DocumentId = document.Id;
        bool waiting = submission.Status == HandoverSubmissionStatus.Submitted
            && document.Status == HandoverDocumentStatus.WaitVerifier
            && submission.Turn.Status == HandoverTurnStatus.IdentityInProgress;
        response.CanApprove = waiting;
        response.CanRequestSupplement = waiting;
        return response;
    }

    /// <summary>Chi tiết hồ sơ không có biên bản (luồng cũ): chỉ duyệt được khi cả ba trường đều khớp.</summary>
    public static VerifierHandoverSubmissionResponse ToVerifierDetail(this HandoverSubmission submission)
    {
        VerifierHandoverSubmissionResponse response = new VerifierHandoverSubmissionResponse();
        FillSummary(response, submission);
        response.Status = submission.Status.ToApiString();
        response.NameMatches = submission.NameMatches;
        response.CitizenIdMatches = submission.CitizenIdMatches;
        response.DateOfBirthMatches = submission.DateOfBirthMatches;
        response.CitizenIdLast4 = submission.CitizenIdLast4;
        response.Acknowledged = submission.Acknowledged;
        response.VoluntaryDecline = submission.VoluntaryDecline;
        response.SupplementReason = submission.SupplementReason;
        response.ItemCount = submission.Turn.Items.Count;
        bool waiting = submission.Status == HandoverSubmissionStatus.Submitted
            && submission.Turn.Status == HandoverTurnStatus.IdentityInProgress;
        response.CanApprove = waiting && response.AllMatch;
        response.CanRequestSupplement = waiting;
        return response;
    }

    private static void FillSummary(VerifierHandoverSubmissionSummaryResponse response, HandoverSubmission submission)
    {
        response.Id = submission.Id;
        response.TurnId = submission.TurnId;
        response.ContainerName = submission.Turn.ContainerName;
        response.Kind = submission.Kind.ToApiString();
        response.AllMatch = submission.NameMatches && submission.CitizenIdMatches && submission.DateOfBirthMatches;
        response.SubmittedAt = submission.CreatedAt;
    }
}
