namespace LegacyVault.Application.ServiceConfig;

/// <summary>
/// Đọc đồng bộ cấu hình ô chính đang có hiệu lực, cho nơi tiêu thụ là thuộc tính đồng bộ đăng ký singleton
/// (tài khoản nhận tiền, khóa webhook SePay). Phần lớn lần đọc lấy thẳng từ cache; chỉ khi cache hết hạn mới hỏi DB.
/// Cài đặt ở Infrastructure.
/// </summary>
public interface ISyncConfigReader
{
    /// <summary>Cấu hình ô chính của dịch vụ (dòng DB đè appsettings); không đọc được thì trả giá trị mặc định của appsettings.</summary>
    ResolvedConfig? Read(string serviceKey);
}
