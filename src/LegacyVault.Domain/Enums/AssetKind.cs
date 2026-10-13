namespace LegacyVault.Domain.Enums;

/// <summary>Loại tài sản số trong kho. Thêm loại mới thì thêm giá trị mới, không đổi giá trị cũ.</summary>
public enum AssetKind
{
    /// <summary>Tệp tải lên (giấy tờ, ảnh, tài liệu...).</summary>
    File = 0,
    /// <summary>Nội dung chữ do Owner nhập (thông tin tài khoản, ghi chú bí mật...). Lưu như một tệp text/plain UTF-8.</summary>
    Text = 1,
}
