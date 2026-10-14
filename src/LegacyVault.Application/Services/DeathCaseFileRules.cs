using System.Text;

namespace LegacyVault.Application.Services;

/// <summary>Kiểm tra tệp giấy chứng tử tải lên (NFR-09, SEC-12).</summary>
public static class DeathCaseFileRules
{
    /// <summary>Số byte đầu cần đọc để nhận loại tệp (WEBP cần 12 byte).</summary>
    public const int HeaderLength = 12;

    /// <summary>Độ dài tối đa của tên tệp lưu trong DB.</summary>
    public const int MaxFileNameLength = 255;

    public const string Pdf = "application/pdf";
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Webp = "image/webp";

    /// <summary>Tên dùng khi tên tệp gửi lên rỗng sau khi làm sạch.</summary>
    public const string DefaultFileName = "giay-chung-tu";

    private static readonly byte[] PdfSignature = { 0x25, 0x50, 0x44, 0x46, 0x2D };
    private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] RiffSignature = { 0x52, 0x49, 0x46, 0x46 };
    private static readonly byte[] WebpSignature = { 0x57, 0x45, 0x42, 0x50 };

    /// <summary>
    /// NFR-09, SEC-12, AC-70: nhận loại tệp theo nội dung thật (chữ ký đầu tệp), không tin đuôi tệp hay Content-Type.
    /// PDF "%PDF-", JPEG FF D8 FF, PNG 89 50 4E 47 0D 0A 1A 0A, WEBP "RIFF" + 4 byte + "WEBP". Khác → null.
    /// </summary>
    public static string? DetectContentType(byte[] header)
    {
        if (StartsWith(header, 0, PdfSignature))
        {
            return Pdf;
        }
        if (StartsWith(header, 0, JpegSignature))
        {
            return Jpeg;
        }
        if (StartsWith(header, 0, PngSignature))
        {
            return Png;
        }
        if (StartsWith(header, 0, RiffSignature) && StartsWith(header, 8, WebpSignature))
        {
            return Webp;
        }
        return null;
    }

    /// <summary>
    /// SEC-12: chống path traversal — chỉ giữ phần sau dấu / hoặc \ cuối cùng, bỏ ký tự điều khiển,
    /// bỏ khoảng trắng đầu/cuối, cắt tối đa 255 ký tự. Rỗng, "." hoặc ".." → tên mặc định.
    /// Tên này chỉ để hiển thị; nội dung lưu theo StorageKey do server sinh.
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (fileName == null)
        {
            return DefaultFileName;
        }

        int lastSeparator = Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\'));
        string baseName = fileName.Substring(lastSeparator + 1);

        StringBuilder builder = new StringBuilder();
        foreach (char c in baseName)
        {
            if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        string cleaned = builder.ToString().Trim();
        if (cleaned.Length == 0 || cleaned == "." || cleaned == "..")
        {
            return DefaultFileName;
        }
        if (cleaned.Length > MaxFileNameLength)
        {
            cleaned = cleaned.Substring(0, MaxFileNameLength);
        }
        return cleaned;
    }

    private static bool StartsWith(byte[] data, int offset, byte[] signature)
    {
        if (data.Length < offset + signature.Length)
        {
            return false;
        }
        for (int i = 0; i < signature.Length; i++)
        {
            if (data[offset + i] != signature[i])
            {
                return false;
            }
        }
        return true;
    }
}
