namespace LegacyVault.Application.Services;

/// <summary>Nội dung tài sản đã mã hóa bị hỏng hoặc không khớp kích thước/mã băm lúc kiểm trước khi tải.</summary>
public sealed class ContentIntegrityException : Exception
{
    public ContentIntegrityException(string message, Exception? inner)
        : base(message, inner)
    {
    }
}
