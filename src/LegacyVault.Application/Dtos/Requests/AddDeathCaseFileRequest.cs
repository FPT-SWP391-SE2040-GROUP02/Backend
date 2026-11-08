namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Dữ liệu tải lên một trang giấy chứng tử. Controller đọc từ multipart/form-data rồi chuyển sang đây
/// (Application không phụ thuộc IFormFile của ASP.NET).
/// </summary>
public class AddDeathCaseFileRequest
{
    /// <summary>Tên tệp do trình duyệt gửi (chưa làm sạch).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// DEATH_CERTIFICATE hoặc DEATH_NOTICE. Có giá trị thì mỗi loại chỉ giữ một tệp (tải lại là thay tệp cũ);
    /// bỏ trống thì như trước: thêm một trang giấy chứng tử.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>Kích thước khai báo của tệp (byte), dùng để chặn sớm trước khi ghi.</summary>
    public long Length { get; set; }

    /// <summary>Nội dung tệp; phải tua về đầu được (CanSeek). Người gọi chịu trách nhiệm đóng stream.</summary>
    public Stream Content { get; set; } = Stream.Null;
}
