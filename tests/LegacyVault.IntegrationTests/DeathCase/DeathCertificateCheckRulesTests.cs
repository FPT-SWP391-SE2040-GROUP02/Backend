using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.DeathCases;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Luật dùng kết quả AI nhận diện giấy chứng tử và cách đọc câu trả lời của Gemini (không cần DB).</summary>
public sealed class DeathCertificateCheckRulesTests
{
    [Fact]
    public void ToStatus_RecognizedOnlyWhenAiSaysYesWithEnoughConfidence()
    {
        Assert.Equal(DeathCertificateCheckStatus.Recognized, DeathCertificateCheckRules.ToStatus(true, 0.7));
        Assert.Equal(DeathCertificateCheckStatus.Recognized, DeathCertificateCheckRules.ToStatus(true, 0.95));
        Assert.Equal(DeathCertificateCheckStatus.NotRecognized, DeathCertificateCheckRules.ToStatus(true, 0.69));
        Assert.Equal(DeathCertificateCheckStatus.NotRecognized, DeathCertificateCheckRules.ToStatus(false, 0.99));
    }

    [Fact]
    public void Combine_OneRecognizedFileIsEnough()
    {
        // Chuẩn bị
        List<DeathCertificateCheckStatus> statuses = new List<DeathCertificateCheckStatus>();
        statuses.Add(DeathCertificateCheckStatus.NotRecognized);
        statuses.Add(DeathCertificateCheckStatus.Unavailable);
        statuses.Add(DeathCertificateCheckStatus.Recognized);

        // Hành động
        DeathCertificateCheckStatus result = DeathCertificateCheckRules.Combine(statuses);

        // Kiểm tra
        Assert.Equal(DeathCertificateCheckStatus.Recognized, result);
    }

    [Fact]
    public void Combine_AllNotRecognizedBlocksSubmit()
    {
        List<DeathCertificateCheckStatus> statuses = new List<DeathCertificateCheckStatus>();
        statuses.Add(DeathCertificateCheckStatus.NotRecognized);
        statuses.Add(DeathCertificateCheckStatus.NotRecognized);

        Assert.Equal(DeathCertificateCheckStatus.NotRecognized, DeathCertificateCheckRules.Combine(statuses));
    }

    [Fact]
    public void Combine_AnyUnavailableWithoutRecognizedLetsSubmitWithFlag()
    {
        List<DeathCertificateCheckStatus> statuses = new List<DeathCertificateCheckStatus>();
        statuses.Add(DeathCertificateCheckStatus.NotRecognized);
        statuses.Add(DeathCertificateCheckStatus.Unavailable);

        Assert.Equal(DeathCertificateCheckStatus.Unavailable, DeathCertificateCheckRules.Combine(statuses));
        Assert.Equal(DeathCertificateCheckStatus.Unavailable,
            DeathCertificateCheckRules.Combine(new List<DeathCertificateCheckStatus>()));
    }

    [Fact]
    public void ParseAnswer_ReadsGeminiJson()
    {
        DeathCertificateCheck yes = GeminiDeathCertificateClassifier.ParseAnswer(
            "{\"isDeathCertificate\": true, \"confidence\": 0.92, \"documentType\": \"Trích lục khai tử\"}");
        Assert.Equal(DeathCertificateCheckStatus.Recognized, yes.Status);
        Assert.Equal("Trích lục khai tử", yes.DocumentType);

        DeathCertificateCheck no = GeminiDeathCertificateClassifier.ParseAnswer(
            "```json\n{\"isDeathCertificate\": false, \"confidence\": 0.9, \"documentType\": \"Căn cước công dân\"}\n```");
        Assert.Equal(DeathCertificateCheckStatus.NotRecognized, no.Status);
        Assert.Equal("Căn cước công dân", no.DocumentType);
    }

    [Fact]
    public void ParseAnswer_BadAnswerIsUnavailableNotApproved()
    {
        Assert.Equal(DeathCertificateCheckStatus.Unavailable,
            GeminiDeathCertificateClassifier.ParseAnswer("Đây là giấy chứng tử").Status);
        Assert.Equal(DeathCertificateCheckStatus.Unavailable,
            GeminiDeathCertificateClassifier.ParseAnswer("{\"isDeathCertificate\": \"yes\", \"confidence\": 1}").Status);
        Assert.Equal(DeathCertificateCheckStatus.Unavailable,
            GeminiDeathCertificateClassifier.ParseAnswer("{\"isDeathCertificate\": true}").Status);
    }
}
