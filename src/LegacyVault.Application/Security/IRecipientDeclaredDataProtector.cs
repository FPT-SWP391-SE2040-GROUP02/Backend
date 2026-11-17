namespace LegacyVault.Application.Security;

/// <summary>Số căn cước và ngày sinh đầy đủ người nhận gõ lúc ký biên bản.</summary>
public sealed class RecipientDeclaredData
{
    public string CitizenId { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }
}

/// <summary>
/// Mã hóa envelope dữ liệu người nhận tự khai trên biên bản (DEK mới mỗi lần, bọc bằng KEK). Kết quả là một chuỗi
/// tự chứa để lưu một cột. Cài đặt ở Infrastructure.
/// </summary>
public interface IRecipientDeclaredDataProtector
{
    string Protect(RecipientDeclaredData data);

    /// <summary>Giải mã. Chuỗi rỗng → null; dữ liệu bị sửa hoặc thiếu KEK → CryptographicException.</summary>
    RecipientDeclaredData? Unprotect(string protectedValue);
}
