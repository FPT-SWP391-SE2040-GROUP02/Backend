namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Dữ liệu tạo tài sản dạng tệp. Controller đọc từ multipart/form-data rồi chuyển sang đây
/// (Application không phụ thuộc IFormFile của ASP.NET).
/// </summary>
/// <remarks>Tên/mô tả không mã hóa: không ghi mật khẩu, seed phrase vào đây (ASSET-02).</remarks>
public class CreateFileAssetRequest
{
    /// <summary>Tên hiển thị; rỗng thì lấy theo tên tệp.</summary>
    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Tên tệp do trình duyệt gửi (chưa làm sạch).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Content-Type do trình duyệt gửi; rỗng thì dùng application/octet-stream.</summary>
    public string? ContentType { get; set; }

    /// <summary>Kích thước khai báo của tệp (byte), dùng để chặn sớm trước khi ghi.</summary>
    public long Length { get; set; }

    /// <summary>Nội dung tệp. Người gọi chịu trách nhiệm đóng stream.</summary>
    public Stream Content { get; set; } = Stream.Null;
}
