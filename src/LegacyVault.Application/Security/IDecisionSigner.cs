using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Security;

/// <summary>
/// DEATH-14: tạo khóa ký, ký và kiểm chữ ký quyết định của Verifier (RSA-2048, SHA-256, PSS). Cài đặt ở Infrastructure.
/// </summary>
/// <remarks>Không bao giờ trả về hay ghi log khóa riêng ở dạng rõ.</remarks>
public interface IDecisionSigner
{
    /// <summary>
    /// Sinh cặp khóa RSA-2048 mới cho Verifier; khóa riêng mã hóa envelope (DEK mới, DEK bọc bằng KEK).
    /// Chỉ tạo entity, người gọi thêm vào repository và lưu.
    /// </summary>
    VerifierSigningKey CreateKey(Guid userId, DateTimeOffset now);

    /// <summary>Ký chuỗi payload bằng khóa riêng của khóa ký. Thiếu KEK hoặc khóa bị sửa → CryptographicException.</summary>
    byte[] Sign(VerifierSigningKey key, byte[] payload);

    /// <summary>
    /// D7: true khi chữ ký đúng với khóa công khai VÀ khóa riêng đã bọc đúng là cặp của khóa công khai đó.
    /// Mọi lỗi giải mã đều trả false, không ném.
    /// </summary>
    bool Verify(VerifierSigningKey key, byte[] payload, byte[] signature);
}
