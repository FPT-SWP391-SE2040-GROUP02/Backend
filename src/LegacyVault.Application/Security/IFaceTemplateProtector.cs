using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Security;

/// <summary>
/// Mã hóa/giải mã vector khuôn mặt của mẫu khuôn mặt Owner (PRIV-03, EKYC-29). Cài đặt ở Infrastructure.
/// </summary>
public interface IFaceTemplateProtector
{
    /// <summary>
    /// Tạo DEK MỚI, mã hóa vector rồi gán vào entity (EncryptionKeyId, WrappedDataKey, EmbeddingEncrypted, Dimension).
    /// Cần template.Id đã có giá trị (dùng trong AAD).
    /// </summary>
    void Protect(OwnerFaceTemplate template, float[] embedding);

    /// <summary>Giải mã vector của mẫu. Dữ liệu bị sửa hoặc thiếu KEK → CryptographicException.</summary>
    float[] Unprotect(OwnerFaceTemplate template);
}
