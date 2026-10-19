using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>Cosine giữa hai vector khuôn mặt, dùng khi so selfie với mẫu khuôn mặt Owner (EKYC-26).</summary>
public sealed class EkycFaceMatchRulesTests
{
    [Fact]
    public void Cosine_SameVector_IsOne()
    {
        float[] vector = FaceTemplateTestSupport.Vector(1);

        Assert.Equal(1.0, EkycRules.CosineSimilarity(vector, vector), 6);
    }

    [Fact]
    public void Cosine_ScaledVector_IsStillOne()
    {
        float[] vector = new float[] { 1f, 2f, 3f };
        float[] scaled = new float[] { 2f, 4f, 6f };

        Assert.Equal(1.0, EkycRules.CosineSimilarity(vector, scaled), 6);
    }

    [Fact]
    public void Cosine_OppositeVector_IsMinusOne()
    {
        Assert.Equal(-1.0, EkycRules.CosineSimilarity(new float[] { 1f, 2f }, new float[] { -1f, -2f }), 6);
    }

    [Fact]
    public void Cosine_Orthogonal_IsZero()
    {
        Assert.Equal(0.0, EkycRules.CosineSimilarity(new float[] { 1f, 0f }, new float[] { 0f, 1f }), 6);
    }

    [Fact]
    public void Cosine_DifferentLength_NeverMatches()
    {
        double score = EkycRules.CosineSimilarity(new float[] { 1f, 2f }, new float[] { 1f, 2f, 3f });

        Assert.True(score < EkycRules.MatchThreshold);
    }

    [Fact]
    public void Cosine_ZeroVector_NeverMatches()
    {
        double score = EkycRules.CosineSimilarity(new float[] { 0f, 0f }, new float[] { 1f, 1f });

        Assert.True(score < EkycRules.MatchThreshold);
    }

    [Fact]
    public void Cosine_TwoUnrelatedVectors_AreBelowThreshold()
    {
        double score = EkycRules.CosineSimilarity(FaceTemplateTestSupport.Vector(1), FaceTemplateTestSupport.Vector(2));

        Assert.True(score < EkycRules.MatchThreshold);
    }
}
