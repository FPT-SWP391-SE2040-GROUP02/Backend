using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Exceptions;

namespace LegacyVault.Infrastructure.Storage;

/// <summary>
/// Mã hóa/giải mã nội dung tài sản bằng AES-256-GCM theo từng khối 64 KiB,
/// để tệp lớn (tới 500 MiB) không phải nạp hết vào RAM.
/// </summary>
/// <remarks>
/// Định dạng tệp đã mã hóa:
/// <code>
/// [header 8 byte]  = "LVA1" (4 byte ASCII) + kích thước khối (int32 big-endian)
/// [khối 0][khối 1]...[khối cuối]
/// mỗi khối         = ciphertext (bằng độ dài nội dung của khối) + tag GCM 16 byte
/// </code>
/// - Khối thường chứa đúng ChunkSize byte nội dung. Khối cuối chứa ít hơn ChunkSize byte (có thể 0 byte).
/// - Nonce gắn số thứ tự khối nên đổi thứ tự khối làm sai tag. Tệp phải kết thúc bằng một khối ngắn hơn
///   ChunkSize (có thể chỉ có tag), nên cắt bớt tệp đúng ranh giới khối cũng bị phát hiện.
/// - Nonce 12 byte = 4 byte 0 + số thứ tự khối (8 byte big-endian). Dùng nonce đếm tăng là an toàn
///   vì mỗi phiên bản nội dung có DEK riêng, một DEK không bao giờ mã hóa hai tệp.
/// </remarks>
public static class ChunkedAesGcm
{
    public const int ChunkSize = 64 * 1024;
    public const int TagSize = 16;
    public const int HeaderSize = 8;
    private const int NonceSize = 12;
    private const int MaxChunkSize = 16 * 1024 * 1024;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("LVA1");

    /// <summary>
    /// Đọc hết input, mã hóa bằng dataKey và ghi ra output.
    /// Trả số byte nội dung gốc. Nội dung gốc vượt maxPlainBytes → AppException FILE_TOO_LARGE.
    /// </summary>
    public static async Task<long> EncryptAsync(
        Stream input, Stream output, byte[] dataKey, long maxPlainBytes, CancellationToken ct)
    {
        byte[] header = CreateHeader(ChunkSize);
        await output.WriteAsync(header, ct);

        byte[] plainChunk = new byte[ChunkSize];
        byte[] cipherChunk = new byte[ChunkSize];
        byte[] tag = new byte[TagSize];
        long totalBytes = 0;
        long chunkIndex = 0;

        using (AesGcm aes = new AesGcm(dataKey, TagSize))
        {
            bool isFinal = false;
            while (!isFinal)
            {
                int length = await ReadFullAsync(input, plainChunk, ct);
                totalBytes += length;
                if (totalBytes > maxPlainBytes)
                {
                    throw new AppException(AppErrorType.PayloadTooLarge, ErrorCode.FileTooLarge,
                        "Tệp vượt quá dung lượng cho phép.");
                }
                // Đọc không đầy khối nghĩa là đã hết input: đây là khối cuối.
                isFinal = length < ChunkSize;
                aes.Encrypt(
                    CreateNonce(chunkIndex),
                    plainChunk.AsSpan(0, length),
                    cipherChunk.AsSpan(0, length),
                    tag);

                await output.WriteAsync(cipherChunk.AsMemory(0, length), ct);
                await output.WriteAsync(tag, ct);
                chunkIndex++;
            }
        }

        CryptographicOperations.ZeroMemory(plainChunk);
        return totalBytes;
    }

    /// <summary>
    /// Giải mã input (định dạng ở trên) bằng dataKey, ghi nội dung gốc ra output.
    /// Trả số byte nội dung gốc. Tệp bị sửa/cắt bớt/sai khóa → CryptographicException.
    /// </summary>
    /// <remarks>
    /// Nội dung được ghi ra theo từng khối đã xác thực; nếu một khối phía sau bị lỗi thì output đã nhận
    /// các khối trước đó. Người gọi (ví dụ endpoint tải xuống) phải coi cả lần đọc là thất bại khi có exception.
    /// </remarks>
    public static async Task<long> DecryptAsync(
        Stream input, Stream output, byte[] dataKey, CancellationToken ct)
    {
        byte[] header = new byte[HeaderSize];
        int headerLength = await ReadFullAsync(input, header, ct);
        if (headerLength != HeaderSize || !header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new CryptographicException("Tệp nội dung không đúng định dạng mã hóa.");
        }
        int chunkSize = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(Magic.Length));
        if (chunkSize <= 0 || chunkSize > MaxChunkSize)
        {
            throw new CryptographicException("Kích thước khối trong tệp nội dung không hợp lệ.");
        }

        byte[] sealedChunk = new byte[chunkSize + TagSize];
        byte[] plainChunk = new byte[chunkSize];
        long totalBytes = 0;
        long chunkIndex = 0;

        using (AesGcm aes = new AesGcm(dataKey, TagSize))
        {
            bool isFinal = false;
            while (!isFinal)
            {
                int sealedLength = await ReadFullAsync(input, sealedChunk, ct);
                if (sealedLength < TagSize)
                {
                    throw new CryptographicException("Tệp nội dung bị cắt bớt.");
                }

                isFinal = sealedLength < sealedChunk.Length;
                int length = sealedLength - TagSize;
                aes.Decrypt(
                    CreateNonce(chunkIndex),
                    sealedChunk.AsSpan(0, length),
                    sealedChunk.AsSpan(length, TagSize),
                    plainChunk.AsSpan(0, length));

                await output.WriteAsync(plainChunk.AsMemory(0, length), ct);
                totalBytes += length;
                chunkIndex++;
            }
        }

        CryptographicOperations.ZeroMemory(plainChunk);
        return totalBytes;
    }

    /// <summary>Kích thước tệp đã mã hóa cho một nội dung gốc dài plainBytes byte.</summary>
    public static long EncryptedSize(long plainBytes)
    {
        long chunkCount = plainBytes / ChunkSize + 1;
        return HeaderSize + plainBytes + chunkCount * TagSize;
    }

    private static byte[] CreateHeader(int chunkSize)
    {
        byte[] header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(Magic.Length), chunkSize);
        return header;
    }

    private static byte[] CreateNonce(long chunkIndex)
    {
        byte[] nonce = new byte[NonceSize];
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(4), chunkIndex);
        return nonce;
    }

    /// <summary>Đọc tới khi đầy buffer hoặc hết stream. Trả số byte đã đọc.</summary>
    private static async Task<int> ReadFullAsync(Stream input, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await input.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }
}
