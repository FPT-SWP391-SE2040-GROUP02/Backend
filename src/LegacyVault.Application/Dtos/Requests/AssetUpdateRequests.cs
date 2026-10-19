using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của PUT /api/v1/vaults/{vaultId}/assets/{assetId}: đổi tên và/hoặc mô tả (ASSET-04).
/// Không gửi (null) = giữ nguyên; description "" = xóa mô tả. Tên/mô tả không mã hóa, không ghi bí mật vào đây.
/// </summary>
public class UpdateAssetRequest
{
    [MaxLength(200, ErrorMessage = "Tên tài sản tối đa 200 ký tự.")]
    public string? Name { get; set; }

    [MaxLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? Description { get; set; }

    /// <summary>etag của AssetResponse đã đọc; lệch thì 409 EDIT_CONFLICT.</summary>
    [Required(ErrorMessage = "Thiếu etag.")]
    public string Etag { get; set; } = string.Empty;
}

/// <summary>
/// Body của POST /api/v1/vaults/{vaultId}/assets/{assetId}/versions/text: thay nội dung tài sản dạng chữ
/// bằng một phiên bản mới (ASSET-04). Phiên bản cũ vẫn được giữ cho snapshot và quota.
/// </summary>
public class CreateTextAssetVersionRequest
{
    [Required(ErrorMessage = "Nội dung là bắt buộc.")]
    [MaxLength(100_000, ErrorMessage = "Nội dung tối đa 100.000 ký tự.")]
    public string Content { get; set; } = string.Empty;

    [Required(ErrorMessage = "Thiếu etag.")]
    public string Etag { get; set; } = string.Empty;
}

/// <summary>
/// Dữ liệu thay nội dung tài sản dạng tệp. Controller đọc từ multipart/form-data rồi chuyển sang đây
/// (Application không phụ thuộc IFormFile của ASP.NET).
/// </summary>
public class CreateFileAssetVersionRequest
{
    public string Etag { get; set; } = string.Empty;

    /// <summary>Tên tệp do trình duyệt gửi (chưa làm sạch).</summary>
    public string FileName { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    /// <summary>Kích thước khai báo của tệp (byte), dùng để chặn sớm trước khi ghi.</summary>
    public long Length { get; set; }

    /// <summary>Nội dung tệp. Người gọi chịu trách nhiệm đóng stream.</summary>
    public Stream Content { get; set; } = Stream.Null;
}
