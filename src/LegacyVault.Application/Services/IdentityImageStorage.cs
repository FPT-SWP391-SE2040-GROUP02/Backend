using System.Security.Cryptography;
using LegacyVault.Application.Storage;

namespace LegacyVault.Application.Services;

/// <summary>
/// Lưu, đọc và xóa các ảnh nhạy cảm qua cùng kho lưu trữ mã hóa envelope với tài sản: ảnh căn cước lúc eKYC,
/// ảnh căn cước hai mặt trong hồ sơ người nhận và ảnh văn bản (biên bản, văn bản từ chối) do hệ thống dựng.
/// Ảnh không có link công khai; mỗi API tải ảnh tự kiểm tra quyền người xem.
/// </summary>
/// <remarks>
/// - Loại ảnh nhận theo chữ ký đầu tệp (JPEG/PNG/WebP), không tin Content-Type của request.
/// - Không log nội dung, tên tệp hay khóa lưu trữ.
/// </remarks>
public static class IdentityImageStorage
{
    /// <summary>Loại ảnh theo nội dung thật; null khi không phải JPEG/PNG/WebP (khi đó không lưu ảnh).</summary>
    public static string? DetectContentType(byte[] image)
    {
        int length = Math.Min(image.Length, DeathCaseFileRules.HeaderLength);
        byte[] header = new byte[DeathCaseFileRules.HeaderLength];
        Array.Copy(image, header, length);
        return AvatarRules.DetectImageContentType(header);
    }

    /// <summary>Mã hóa và ghi ảnh thành một object mới; trả tham chiếu để chép vào các cột *Image* của entity.</summary>
    public static async Task<StoredContent> SaveAsync(IAssetContentStore contentStore, byte[] image, CancellationToken ct)
    {
        using (MemoryStream stream = new MemoryStream(image, false))
        {
            return await contentStore.SaveAsync(stream, EkycRules.MaxImageBytes, ct);
        }
    }

    /// <summary>
    /// Kiểm toàn vẹn (giải mã vào Stream.Null) rồi trả bản tải giải mã lần hai thẳng ra response, giống trang giấy
    /// chứng tử. Hỏng → ContentIntegrityException.
    /// </summary>
    public static async Task<PreparedDownload> PrepareAsync(
        IAssetContentStore contentStore, StoredContent stored, string contentType, string fileName, CancellationToken ct)
    {
        try
        {
            await contentStore.CopyToAsync(stored, Stream.Null, ct);
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException)
        {
            throw new ContentIntegrityException("Ảnh danh tính không vượt qua kiểm tra toàn vẹn.", ex);
        }

        PreparedDownload download = new PreparedDownload
        {
            FileName = fileName,
            ContentType = contentType,
            Length = stored.SizeBytes,
            WriteToAsync = (destination, token) => contentStore.CopyToAsync(stored, destination, token)
        };
        return download;
    }

    /// <summary>Xóa object SAU khi bản ghi đã lưu, hoặc bù trừ khi lưu thất bại. Lỗi thì để lại object mồ côi (đã mã hóa).</summary>
    public static async Task TryDeleteAsync(IAssetContentStore contentStore, string? storageKey)
    {
        if (string.IsNullOrEmpty(storageKey))
        {
            return;
        }
        try
        {
            await contentStore.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception)
        {
            // Cố ý bỏ qua: không che việc chính đã thành công; object còn lại cần dọn thủ công.
        }
    }
}
