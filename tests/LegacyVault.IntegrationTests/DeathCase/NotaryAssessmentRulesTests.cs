using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Câu hỏi gửi AI và gợi ý dự phòng cho Người xét duyệt: AI chỉ gợi ý, không khẳng định điều chưa kiểm tra.</summary>
public sealed class NotaryAssessmentRulesTests
{
    private const string FullCitizenId = "079206021793";

    private static VerifierDeathCaseResponse NewCase(bool withFile, bool canApprove)
    {
        VerifierDeathCaseResponse detail = new VerifierDeathCaseResponse();
        detail.Id = Guid.NewGuid();
        detail.Status = "UNDER_REVIEW";
        detail.CanApprove = canApprove;
        detail.ReviewVersion.VersionNo = 1;
        detail.ReviewVersion.OwnerFullName = "Nguyễn Văn An";
        detail.ReviewVersion.OwnerCitizenId = FullCitizenId;
        detail.ReviewVersion.OwnerCitizenIdLast4 = "1793";
        detail.ReviewVersion.DateOfDeath = new DateOnly(2026, 10, 5);
        detail.ReviewVersion.IssuedOn = new DateOnly(2026, 10, 6);
        detail.ReviewVersion.CertificateNumber = "123/2026";
        detail.ReviewVersion.IssuedBy = "UBND phường Bến Nghé";
        if (withFile)
        {
            detail.ReviewVersion.Files.Add(new DeathCaseFileResponse());
        }
        return detail;
    }

    private static VerifierFlagResponse NewFlag(string kind, bool reviewed, string? reviewNote)
    {
        VerifierFlagResponse flag = new VerifierFlagResponse();
        flag.Id = Guid.NewGuid();
        flag.Kind = kind;
        flag.Detail = "Chi tiết cờ " + kind;
        if (reviewed)
        {
            flag.ReviewedAt = DateTimeOffset.UtcNow;
        }
        flag.ReviewNote = reviewNote;
        return flag;
    }

    [Fact]
    public void BuildPrompt_MasksCitizenIdAndNeverSendsTheFullNumber()
    {
        VerifierDeathCaseResponse detail = NewCase(true, true);

        string prompt = NotaryAssessmentRules.BuildPrompt(detail);

        Assert.DoesNotContain(FullCitizenId, prompt);
        Assert.Contains("****1793", prompt);
    }

    [Fact]
    public void BuildPrompt_DoesNotClaimEkycAndDoesNotForwardTheVerifiersOwnNote()
    {
        VerifierDeathCaseResponse detail = NewCase(true, false);
        detail.Flags.Add(NewFlag("CERTIFICATE_UNVERIFIED", true, "ghi chu rieng cua verifier"));

        string prompt = NotaryAssessmentRules.BuildPrompt(detail);

        Assert.DoesNotContain("Đã xác minh định danh eKYC", prompt);
        Assert.DoesNotContain("ghi chu rieng cua verifier", prompt);
        Assert.Contains("Người xét duyệt đã xem xét", prompt);
    }

    [Fact]
    public void BuildPrompt_TellsAiItIsAdvisoryAndWritesForTheExecutor()
    {
        VerifierDeathCaseResponse detail = NewCase(true, true);

        string prompt = NotaryAssessmentRules.BuildPrompt(detail);

        Assert.Contains("KHÔNG có quyền quyết định", prompt);
        Assert.Contains("NGƯỜI THỰC HIỆN", prompt);
        Assert.Contains("không mở đầu bằng 'Kính gửi chủ kho'", prompt);
    }

    [Fact]
    public void Fallback_FlagsLeftUnreviewed_RecommendsSupplementWithAMatchingNote()
    {
        VerifierDeathCaseResponse detail = NewCase(true, false);
        detail.Flags.Add(NewFlag("CERTIFICATE_UNVERIFIED", false, null));

        AiDossierAnalysisResponse result = NotaryAssessmentRules.BuildFallback(detail, DateTimeOffset.UtcNow);

        Assert.Equal("SUPPLEMENT", result.Recommendation);
        Assert.DoesNotContain("đầy đủ, hợp lệ", result.ProposedDecisionNote);
        Assert.Contains("chưa được Người xét duyệt xem xét", result.RecommendationReason);
    }

    [Fact]
    public void Fallback_FlagsAlreadyReviewed_NoLongerPushToSupplement()
    {
        VerifierDeathCaseResponse detail = NewCase(true, true);
        detail.Flags.Add(NewFlag("CERTIFICATE_UNVERIFIED", true, "đã xem"));

        AiDossierAnalysisResponse result = NotaryAssessmentRules.BuildFallback(detail, DateTimeOffset.UtcNow);

        Assert.Equal("APPROVE", result.Recommendation);
        Assert.Equal("LOW", result.RiskLevel);
    }

    [Fact]
    public void Fallback_CheckinAfterDeath_RecommendsRejectInNeutralWordsForTheExecutor()
    {
        VerifierDeathCaseResponse detail = NewCase(true, false);
        detail.Flags.Add(NewFlag("CHECKIN_AFTER_DEATH", false, null));

        AiDossierAnalysisResponse result = NotaryAssessmentRules.BuildFallback(detail, DateTimeOffset.UtcNow);

        Assert.Equal("REJECT", result.Recommendation);
        Assert.Equal("HIGH", result.RiskLevel);
        Assert.DoesNotContain("Kính gửi chủ kho", result.ProposedDecisionNote);
        Assert.DoesNotContain("gian lận", result.ProposedDecisionNote);
    }

    [Fact]
    public void Fallback_NoFiles_AsksForTheCertificateImage()
    {
        VerifierDeathCaseResponse detail = NewCase(false, false);

        AiDossierAnalysisResponse result = NotaryAssessmentRules.BuildFallback(detail, DateTimeOffset.UtcNow);

        Assert.Equal("SUPPLEMENT", result.Recommendation);
        Assert.Contains("tải lên ảnh chụp rõ nét", result.ProposedDecisionNote);
    }

    [Fact]
    public void Fallback_NeverClaimsMatchesItDidNotCheck()
    {
        VerifierDeathCaseResponse detail = NewCase(true, true);

        AiDossierAnalysisResponse result = NotaryAssessmentRules.BuildFallback(detail, DateTimeOffset.UtcNow);

        foreach (AiDossierChecklistItem item in result.Checklist)
        {
            Assert.DoesNotContain("trùng khớp", item.Detail);
        }
        AiDossierChecklistItem documents = result.Checklist[2];
        Assert.Equal("WARNING", documents.Status);
        Assert.Contains("AI không xem ảnh", documents.Detail);
        Assert.Equal("rule-based-fallback", result.ModelName);
    }

    [Fact]
    public void LimitNote_CutsToTheDecisionReasonLimitAndFillsAnEmptyNote()
    {
        string longNote = new string('a', 1500);

        Assert.Equal(NotaryAssessmentRules.MaxNoteLength, NotaryAssessmentRules.LimitNote(longNote).Length);
        Assert.Equal(NotaryAssessmentRules.DefaultNote, NotaryAssessmentRules.LimitNote(null));
        Assert.Equal(NotaryAssessmentRules.DefaultNote, NotaryAssessmentRules.LimitNote("   "));
    }
}
