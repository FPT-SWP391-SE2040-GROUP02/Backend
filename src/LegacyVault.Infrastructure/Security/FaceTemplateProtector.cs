using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Storage;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Mã hóa envelope vector khuôn mặt của Owner (PRIV-03, EKYC-29), dùng lại DataKeyProtector của tài sản.
/// </summary>
/// <remarks>
/// - Mỗi lần Protect sinh một DEK mới (32 byte); DB chỉ lưu DEK đã bọc bằng KEK.
/// - Vector được ghi thành các số thực 32 bit little-endian rồi mã hóa AES-256-GCM; kết quả = nonce (12) + ciphertext + tag (16).
/// - AAD = "face-template:{Id}:{số chiều}": chép cột sang mẫu khác hoặc đổi số chiều thì giải mã thất bại.
/// - DEK và bản rõ tạm được xóa khỏi RAM ngay sau khi dùng. Dữ liệu bị sửa → CryptographicException, không nuốt lỗi.
/// </remarks>
public sealed class FaceTemplateProtector : IFaceTemplateProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int FloatSize = sizeof(float);

    private readonly DataKeyProtector dataKeyProtector;

    public FaceTemplateProtector(DataKeyProtector dataKeyProtector)
    {
        this.dataKeyProtector = dataKeyProtector;
    }

    public void Protect(OwnerFaceTemplate template, float[] embedding)
    {
        if (embedding.Length == 0)
        {
            throw new ArgumentException("Vector khuôn mặt rỗng.", nameof(embedding));
        }

        byte[] dataKey = dataKeyProtector.CreateDataKey();
        byte[] plain = new byte[embedding.Length * FloatSize];
        try
        {
            for (int index = 0; index < embedding.Length; index++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(plain.AsSpan(index * FloatSize, FloatSize), embedding[index]);
            }

            WrappedDataKey wrapped = dataKeyProtector.Wrap(dataKey);
            template.EncryptionKeyId = wrapped.KeyId;
            template.WrappedDataKey = wrapped.Value;
            template.Dimension = embedding.Length;
            template.EmbeddingEncrypted = Encrypt(dataKey, plain, BuildAad(template.Id, embedding.Length));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public float[] Unprotect(OwnerFaceTemplate template)
    {
        byte[] dataKey = dataKeyProtector.Unwrap(template.EncryptionKeyId, template.WrappedDataKey);
        byte[] plain = Array.Empty<byte>();
        try
        {
            plain = Decrypt(dataKey, template.EmbeddingEncrypted, BuildAad(template.Id, template.Dimension));
            if (plain.Length != template.Dimension * FloatSize)
            {
                throw new CryptographicException("Mẫu khuôn mặt sau giải mã có độ dài không hợp lệ.");
            }

            float[] embedding = new float[template.Dimension];
            for (int index = 0; index < embedding.Length; index++)
            {
                embedding[index] = BinaryPrimitives.ReadSingleLittleEndian(plain.AsSpan(index * FloatSize, FloatSize));
            }
            return embedding;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] BuildAad(Guid templateId, int dimension)
    {
        return Encoding.UTF8.GetBytes($"face-template:{templateId}:{dimension}");
    }

    /// <summary>Kết quả = nonce (12) + ciphertext (cùng độ dài bản rõ) + tag (16).</summary>
    private static byte[] Encrypt(byte[] dataKey, byte[] plainText, byte[] aad)
    {
        byte[] result = new byte[NonceSize + plainText.Length + TagSize];
        Span<byte> nonce = result.AsSpan(0, NonceSize);
        Span<byte> cipherText = result.AsSpan(NonceSize, plainText.Length);
        Span<byte> tag = result.AsSpan(NonceSize + plainText.Length, TagSize);
        RandomNumberGenerator.Fill(nonce);

        using (AesGcm aes = new AesGcm(dataKey, TagSize))
        {
            aes.Encrypt(nonce, plainText, cipherText, tag, aad);
        }
        return result;
    }

    /// <summary>Sai tag (dữ liệu/AAD bị sửa) → AuthenticationTagMismatchException (là CryptographicException).</summary>
    private static byte[] Decrypt(byte[] dataKey, byte[] encrypted, byte[] aad)
    {
        if (encrypted.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Mẫu khuôn mặt đã mã hóa có độ dài không hợp lệ.");
        }

        int cipherLength = encrypted.Length - NonceSize - TagSize;
        byte[] plainText = new byte[cipherLength];
        using (AesGcm aes = new AesGcm(dataKey, TagSize))
        {
            aes.Decrypt(
                encrypted.AsSpan(0, NonceSize),
                encrypted.AsSpan(NonceSize, cipherLength),
                encrypted.AsSpan(NonceSize + cipherLength, TagSize),
                plainText,
                aad);
        }
        return plainText;
    }
}
