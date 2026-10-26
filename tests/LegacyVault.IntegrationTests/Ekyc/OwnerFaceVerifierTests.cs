using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Ekyc;
using LegacyVault.Infrastructure.Security;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>So selfie Owner với mẫu khuôn mặt đã lưu (ALIVE-02, EKYC-26, EKYC-19).</summary>
public sealed class OwnerFaceVerifierTests
{
    private static readonly byte[] Selfie = new byte[] { 1, 2, 3 };

    private readonly FakeOwnerFaceTemplateRepository templates = new FakeOwnerFaceTemplateRepository();
    private readonly FaceTemplateProtector protector = FaceTemplateTestSupport.CreateProtector();
    private readonly FakeEkycClient client = new FakeEkycClient();
    private readonly OwnerFaceVerifier verifier;
    private readonly Guid ownerId = Guid.NewGuid();

    public OwnerFaceVerifierTests()
    {
        verifier = new OwnerFaceVerifier(templates, protector, client, new EkycOptions());
    }

    private void StoreTemplate(float[] vector, string modelName, int version)
    {
        OwnerFaceTemplate template = new OwnerFaceTemplate();
        template.UserId = ownerId;
        template.Version = version;
        template.ModelName = modelName;
        protector.Protect(template, vector);
        templates.Templates.Add(template);
    }

    private Task<OwnerFaceOutcome> VerifyAsync()
    {
        return verifier.VerifyAsync(ownerId, Selfie, "selfie.jpg", "image/jpeg", CancellationToken.None);
    }

    [Fact]
    public async Task NoTemplate_ReturnsNoTemplate_AndDoesNotCallService()
    {
        OwnerFaceOutcome outcome = await VerifyAsync();

        // Báo riêng (không lẫn với lỗi dịch vụ) để FE hướng Owner đi làm eKYC; vẫn không tính là thất bại (EKYC-19).
        Assert.Equal(OwnerFaceOutcome.NoTemplate, outcome);
        Assert.Equal(0, client.EmbeddingCalls);
    }

    [Fact]
    public async Task SamePerson_IsMatched()
    {
        StoreTemplate(FaceTemplateTestSupport.Vector(1), "sface", 1);
        client.Embedding = new EkycFaceEmbedding { ModelName = "sface", Values = FaceTemplateTestSupport.Vector(1) };

        Assert.Equal(OwnerFaceOutcome.Matched, await VerifyAsync());
    }

    [Fact]
    public async Task DifferentPerson_IsNotMatched()
    {
        StoreTemplate(FaceTemplateTestSupport.Vector(1), "sface", 1);
        client.Embedding = new EkycFaceEmbedding { ModelName = "sface", Values = FaceTemplateTestSupport.Vector(2) };

        Assert.Equal(OwnerFaceOutcome.NotMatched, await VerifyAsync());
    }

    [Fact]
    public async Task OtherOwnersTemplate_IsNotUsed()
    {
        StoreTemplate(FaceTemplateTestSupport.Vector(1), "sface", 1);

        OwnerFaceOutcome outcome = await verifier.VerifyAsync(
            Guid.NewGuid(), Selfie, "selfie.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal(OwnerFaceOutcome.NoTemplate, outcome);
    }

    [Fact]
    public async Task ServiceDown_IsUnavailable_NotAFailure()
    {
        StoreTemplate(FaceTemplateTestSupport.Vector(1), "sface", 1);
        client.EmbeddingError = new AppException(
            AppErrorType.ServiceUnavailable, ErrorCode.EkycServiceUnavailable, "Dịch vụ không khả dụng.");

        Assert.Equal(OwnerFaceOutcome.Unavailable, await VerifyAsync());
    }

    [Fact]
    public async Task ImageWithoutSingleFace_ThrowsImageRejected_SoItIsNotCountedAsFailure()
    {
        StoreTemplate(FaceTemplateTestSupport.Vector(1), "sface", 1);
        client.EmbeddingError = new AppException(
            AppErrorType.Validation, ErrorCode.EkycImageRejected, "Ảnh cần có đúng một khuôn mặt.");

        AppException error = await Assert.ThrowsAsync<AppException>(() => VerifyAsync());

        Assert.Equal(ErrorCode.EkycImageRejected, error.Code);
    }

    [Fact]
    public async Task ModelChanged_IsUnavailable()
    {
        StoreTemplate(FaceTemplateTestSupport.Vector(1), "sface", 1);
        client.Embedding = new EkycFaceEmbedding { ModelName = "other", Values = FaceTemplateTestSupport.Vector(1) };

        Assert.Equal(OwnerFaceOutcome.Unavailable, await VerifyAsync());
    }

    [Fact]
    public async Task UsesLatestTemplateVersion()
    {
        StoreTemplate(FaceTemplateTestSupport.Vector(2), "sface", 1);
        StoreTemplate(FaceTemplateTestSupport.Vector(1), "sface", 2);
        client.Embedding = new EkycFaceEmbedding { ModelName = "sface", Values = FaceTemplateTestSupport.Vector(1) };

        Assert.Equal(OwnerFaceOutcome.Matched, await VerifyAsync());
    }
}
