using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Mappings;

/// <summary>Đổi hồ sơ người nhận sang dữ liệu trả ra màn hình (người nhận và Người xét duyệt). Không có ảnh căn cước.</summary>
public static class HandoverSubmissionMappings
{
    /// <summary>Kết quả trả cho người nhận sau khi nộp hồ sơ hoặc khi xem hồ sơ mới nhất của mình.</summary>
    public static HandoverSubmissionResponse ToResponse(this HandoverSubmission submission)
    {
        return new HandoverSubmissionResponse
        {
            Id = submission.Id,
            TurnId = submission.TurnId,
            Kind = submission.Kind.ToApiString(),
            Status = submission.Status.ToApiString(),
            SupplementReason = submission.SupplementReason,
            CreatedAt = submission.CreatedAt
        };
    }

    /// <summary>Một dòng trong danh sách hồ sơ chờ kiểm tra.</summary>
    public static VerifierHandoverSubmissionSummaryResponse ToVerifierSummary(this HandoverSubmission submission)
    {
        VerifierHandoverSubmissionSummaryResponse response = new VerifierHandoverSubmissionSummaryResponse();
        FillSummary(response, submission);
        return response;
    }

    /// <summary>Chi tiết hồ sơ danh tính: kết quả đối chiếu ba trường chỉ để luật sư tham khảo, luật sư tự quyết xác nhận hay yêu cầu làm lại.</summary>
    public static VerifierHandoverSubmissionResponse ToVerifierDetail(this HandoverSubmission submission)
    {
        VerifierHandoverSubmissionResponse response = new VerifierHandoverSubmissionResponse();
        FillSummary(response, submission);
        response.Status = submission.Status.ToApiString();
        response.NameMatches = submission.NameMatches;
        response.CitizenIdMatches = submission.CitizenIdMatches;
        response.DateOfBirthMatches = submission.DateOfBirthMatches;
        response.CitizenId = submission.CitizenId;
        response.Acknowledged = submission.Acknowledged;
        response.VoluntaryDecline = submission.VoluntaryDecline;
        response.SupplementReason = submission.SupplementReason;
        response.ItemCount = submission.Turn.Items.Count;
        bool waiting = submission.Status == HandoverSubmissionStatus.Submitted
            && submission.Turn.Status == HandoverTurnStatus.IdentityInProgress;
        // Luật sư tự quyết: có trường không khớp vẫn được xác nhận nếu xem ảnh thấy đúng người.
        response.CanApprove = waiting;
        response.CanRequestSupplement = waiting;
        return response;
    }

    /// <summary>Điền các cột chung của một dòng danh sách và của trang chi tiết hồ sơ.</summary>
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
