using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Mappings;

public static class VerifierDeathCaseMappings
{
    public static VerifierDeathCaseSummaryResponse ToResponse(this VerifierCaseRow row)
    {
        VerifierDeathCaseSummaryResponse response = new VerifierDeathCaseSummaryResponse();
        response.Id = row.Id;
        response.Status = row.Status.ToApiString();
        response.ReviewVersionNo = row.ReviewVersionNo;
        response.OwnerCitizenIdLast4 = row.OwnerCitizenIdLast4;
        response.DateOfDeath = row.DateOfDeath;
        response.SubmittedAt = row.SubmittedAt;
        response.AssignedAt = row.AssignedAt;
        response.DueAt = row.DueAt;
        response.ObjectionEndsAt = row.ObjectionEndsAt;
        response.UnreviewedFlagCount = row.UnreviewedFlagCount;
        response.ClosedAt = row.ClosedAt;
        return response;
    }

    /// <summary>
    /// Cần hồ sơ đã nạp Versions.Files, Flags, Notices, Decisions. identity là dữ liệu đã giải mã CỦA BẢN ĐANG XÉT
    /// (null nếu không có). Các bản cũ không giải mã (DATA-02). approveBlockers lấy từ DeathCaseReviewRules.
    /// </summary>
    public static VerifierDeathCaseResponse ToVerifierResponse(
        this DeathCase deathCase,
        DeathCaseAssignment assignment,
        DeathCaseIdentityData? identity,
        PreviousCaseRow? previous,
        List<string> approveBlockers,
        VerifierHandoverResponse? handover)
    {
        DeathCaseVersion reviewVersion = DeathCaseReviewRules.FindReviewVersion(deathCase);

        VerifierReviewVersionResponse review = new VerifierReviewVersionResponse();
        review.VersionNo = reviewVersion.VersionNo;
        review.OwnerCitizenIdLast4 = reviewVersion.OwnerCitizenIdLast4;
        review.DateOfDeath = reviewVersion.DateOfDeath;
        review.IssuedOn = reviewVersion.IssuedOn;
        review.SubmittedAt = reviewVersion.SubmittedAt;
        if (identity != null)
        {
            review.OwnerFullName = identity.OwnerFullName;
            review.OwnerCitizenId = identity.OwnerCitizenId;
            review.CertificateNumber = identity.CertificateNumber;
            review.IssuedBy = identity.IssuedBy;
        }
        review.Files = ToFileResponses(reviewVersion);

        List<DeathCaseVersion> olderVersions = new List<DeathCaseVersion>();
        foreach (DeathCaseVersion version in deathCase.Versions)
        {
            if (version.SubmittedAt != null && version.VersionNo != reviewVersion.VersionNo)
            {
                olderVersions.Add(version);
            }
        }
        olderVersions.Sort((left, right) => left.VersionNo.CompareTo(right.VersionNo));

        VerifierDeathCaseResponse response = new VerifierDeathCaseResponse();
        response.Id = deathCase.Id;
        response.VaultId = deathCase.VaultId;
        response.Status = deathCase.Status.ToApiString();
        response.Etag = Etag.From(deathCase.UpdatedAt);
        response.CreatedAt = deathCase.CreatedAt;
        response.FirstSubmittedAt = deathCase.FirstSubmittedAt;
        response.ClosedAt = deathCase.ClosedAt;
        response.ReviewVersion = review;
        foreach (DeathCaseVersion older in olderVersions)
        {
            VerifierOlderVersionResponse olderResponse = new VerifierOlderVersionResponse();
            olderResponse.VersionNo = older.VersionNo;
            olderResponse.OwnerCitizenIdLast4 = older.OwnerCitizenIdLast4;
            olderResponse.DateOfDeath = older.DateOfDeath;
            olderResponse.SubmittedAt = older.SubmittedAt;
            olderResponse.Files = ToFileResponses(older);
            response.Versions.Add(olderResponse);
        }
        response.Flags = ToFlagResponses(deathCase, reviewVersion);
        response.Decisions = ToDecisionResponses(deathCase);
        response.OwnerNotice = ToNoticeResponse(deathCase);
        response.ObjectionEndsAt = deathCase.ObjectionEndsAt;

        VerifierAssignmentResponse assignmentResponse = new VerifierAssignmentResponse();
        assignmentResponse.AssignedAt = assignment.AssignedAt;
        assignmentResponse.DueAt = assignment.DueAt;
        response.Assignment = assignmentResponse;

        response.CanApprove = approveBlockers.Count == 0;
        response.ApproveBlockers = approveBlockers;
        if (previous != null)
        {
            VerifierPreviousCaseResponse previousResponse = new VerifierPreviousCaseResponse();
            previousResponse.Id = previous.Id;
            previousResponse.ClosedAt = previous.ClosedAt;
            previousResponse.RejectReason = previous.RejectReason;
            response.PreviousCase = previousResponse;
        }
        response.Handover = handover;
        return response;
    }

    private static List<DeathCaseFileResponse> ToFileResponses(DeathCaseVersion version)
    {
        List<DeathCaseFile> orderedFiles = new List<DeathCaseFile>(version.Files);
        orderedFiles.Sort((left, right) => left.Position.CompareTo(right.Position));
        List<DeathCaseFileResponse> responses = new List<DeathCaseFileResponse>();
        foreach (DeathCaseFile file in orderedFiles)
        {
            DeathCaseFileResponse fileResponse = new DeathCaseFileResponse();
            fileResponse.Id = file.Id;
            fileResponse.Position = file.Position;
            fileResponse.FileName = file.FileName;
            fileResponse.ContentType = file.ContentType;
            fileResponse.SizeBytes = file.SizeBytes;
            fileResponse.UploadedAt = file.UploadedAt;
            responses.Add(fileResponse);
        }
        return responses;
    }

    /// <summary>DEATH-10: chỉ cờ của bản đang xét.</summary>
    private static List<VerifierFlagResponse> ToFlagResponses(DeathCase deathCase, DeathCaseVersion reviewVersion)
    {
        List<DeathCaseFlag> flags = new List<DeathCaseFlag>();
        foreach (DeathCaseFlag flag in deathCase.Flags)
        {
            if (flag.VersionId == reviewVersion.Id)
            {
                flags.Add(flag);
            }
        }
        flags.Sort((left, right) =>
        {
            int byTime = left.CreatedAt.CompareTo(right.CreatedAt);
            if (byTime != 0)
            {
                return byTime;
            }
            return left.Id.CompareTo(right.Id);
        });

        List<VerifierFlagResponse> responses = new List<VerifierFlagResponse>();
        foreach (DeathCaseFlag flag in flags)
        {
            VerifierFlagResponse flagResponse = new VerifierFlagResponse();
            flagResponse.Id = flag.Id;
            flagResponse.Kind = flag.Kind.ToApiString();
            flagResponse.Detail = flag.Detail;
            flagResponse.CreatedAt = flag.CreatedAt;
            flagResponse.ReviewedAt = flag.ReviewedAt;
            flagResponse.ReviewNote = flag.ReviewNote;
            responses.Add(flagResponse);
        }
        return responses;
    }

    private static List<VerifierDecisionResponse> ToDecisionResponses(DeathCase deathCase)
    {
        List<DeathCaseDecision> decisions = new List<DeathCaseDecision>(deathCase.Decisions);
        decisions.Sort(CompareDecisions);

        List<VerifierDecisionResponse> responses = new List<VerifierDecisionResponse>();
        foreach (DeathCaseDecision decision in decisions)
        {
            VerifierDecisionResponse decisionResponse = new VerifierDecisionResponse();
            decisionResponse.Kind = decision.Kind.ToApiString();
            decisionResponse.VersionNo = decision.VersionNo;
            decisionResponse.Reason = decision.Reason;
            decisionResponse.RequestedParts = DeathCaseReviewRules.SplitRequestedParts(decision.RequestedParts);
            decisionResponse.DecidedAt = decision.DecidedAt;
            responses.Add(decisionResponse);
        }
        return responses;
    }

    /// <summary>Cũ → mới theo DecidedAt, rồi theo Id.</summary>
    public static int CompareDecisions(DeathCaseDecision left, DeathCaseDecision right)
    {
        int byTime = left.DecidedAt.CompareTo(right.DecidedAt);
        if (byTime != 0)
        {
            return byTime;
        }
        return left.Id.CompareTo(right.Id);
    }

    /// <summary>Thông báo của bản NotifiedVersionNo (nhiều dòng thì lấy dòng tạo sau). Chưa có → null.</summary>
    private static DeathCaseNoticeResponse? ToNoticeResponse(DeathCase deathCase)
    {
        if (deathCase.NotifiedVersionNo == null)
        {
            return null;
        }
        DeathCaseNotice? latest = null;
        foreach (DeathCaseNotice notice in deathCase.Notices)
        {
            if (notice.VersionNo != deathCase.NotifiedVersionNo.Value)
            {
                continue;
            }
            if (latest == null || notice.CreatedAt > latest.CreatedAt)
            {
                latest = notice;
            }
        }
        if (latest == null)
        {
            return null;
        }

        DeathCaseNoticeResponse response = new DeathCaseNoticeResponse();
        response.Status = latest.Status.ToApiString();
        response.DeliveredAt = latest.DeliveredAt;
        return response;
    }
}
