namespace LegacyVault.Domain.Entities;

/// <summary>
/// DEATH-14: cặp khóa RSA ký số quyết định của một Verifier (bảng VerifierSigningKeys).
/// </summary>
/// <remarks>
/// - Mỗi Verifier tối đa một khóa đang dùng (RevokedAt null), giữ bằng chỉ mục unique có lọc.
/// - Khóa riêng (PKCS#8) mã hóa envelope: DEK mới mã hóa khóa riêng bằng AES-256-GCM, DEK bọc bằng KEK.
///   Không bao giờ lưu, ghi log hay trả về khóa riêng ở dạng rõ.
/// - Khóa tạo tự động ở lần ký đầu, lưu cùng lần lưu với quyết định.
/// </remarks>
public class VerifierSigningKey
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    /// <summary>Thuật toán ký, hiện là "RSA-2048-PSS-SHA256".</summary>
    public string Algorithm { get; set; } = string.Empty;

    /// <summary>Khóa công khai dạng SubjectPublicKeyInfo (DER).</summary>
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();

    /// <summary>Id của KEK đã dùng để bọc DEK.</summary>
    public string EncryptionKeyId { get; set; } = string.Empty;

    /// <summary>DEK đã bọc bằng KEK.</summary>
    public byte[] WrappedDataKey { get; set; } = Array.Empty<byte>();

    /// <summary>Khóa riêng PKCS#8 đã mã hóa: nonce + ciphertext + tag.</summary>
    public byte[] PrivateKeyEncrypted { get; set; } = Array.Empty<byte>();

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Lúc thu hồi khóa; null = đang dùng. Chưa có API thu hồi.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
