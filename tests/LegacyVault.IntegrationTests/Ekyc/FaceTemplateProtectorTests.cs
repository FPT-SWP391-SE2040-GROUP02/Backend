using System.Security.Cryptography;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Security;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>Mã hóa vector khuôn mặt Owner (PRIV-03, EKYC-29): đúng chiều, không lộ bản rõ, phát hiện sửa/đổi chỗ.</summary>
public sealed class FaceTemplateProtectorTests
{
    private readonly FaceTemplateProtector protector = FaceTemplateTestSupport.CreateProtector();

    private OwnerFaceTemplate Protected(float[] vector)
    {
        OwnerFaceTemplate template = new OwnerFaceTemplate();
        protector.Protect(template, vector);
        return template;
    }

    [Fact]
    public void RoundTrip_ReturnsSameVector()
    {
        float[] vector = FaceTemplateTestSupport.Vector(7);
        OwnerFaceTemplate template = Protected(vector);

        float[] result = protector.Unprotect(template);

        Assert.Equal(vector, result);
        Assert.Equal(128, template.Dimension);
        Assert.Equal(60, template.WrappedDataKey.Length);
        Assert.Equal("test-key", template.EncryptionKeyId);
    }

    [Fact]
    public void Ciphertext_DoesNotContainPlainVectorBytes()
    {
        float[] vector = FaceTemplateTestSupport.Vector(7);
        byte[] plain = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, plain, 0, plain.Length);

        OwnerFaceTemplate template = Protected(vector);

        Assert.Equal(12 + plain.Length + 16, template.EmbeddingEncrypted.Length);
        Assert.False(template.EmbeddingEncrypted.AsSpan().IndexOf(plain.AsSpan(0, 16)) >= 0);
    }

    [Fact]
    public void EachProtect_UsesNewKeyAndNonce()
    {
        float[] vector = FaceTemplateTestSupport.Vector(7);

        OwnerFaceTemplate first = Protected(vector);
        OwnerFaceTemplate second = Protected(vector);

        Assert.NotEqual(first.WrappedDataKey, second.WrappedDataKey);
        Assert.NotEqual(first.EmbeddingEncrypted, second.EmbeddingEncrypted);
    }

    [Fact]
    public void TamperedCiphertext_Throws()
    {
        OwnerFaceTemplate template = Protected(FaceTemplateTestSupport.Vector(7));
        template.EmbeddingEncrypted[20] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(template));
    }

    [Fact]
    public void ChangedDimension_Throws()
    {
        OwnerFaceTemplate template = Protected(FaceTemplateTestSupport.Vector(7));
        template.Dimension = 64;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(template));
    }

    [Fact]
    public void EmptyVector_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => protector.Protect(new OwnerFaceTemplate(), Array.Empty<float>()));
    }
}
