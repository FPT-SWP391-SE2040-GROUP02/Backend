namespace LegacyVault.Application.ServiceConfig;

/// <summary>Giá trị cấu hình mặc định của dịch vụ ngoài đọc từ appsettings/biến môi trường. Cài đặt ở Infrastructure.</summary>
public interface ISystemConfigDefaults
{
    /// <summary>Cấu hình ô chính theo appsettings; dịch vụ chưa nối thì null. Nguồn luôn là APPSETTINGS.</summary>
    ResolvedConfig? ReadPrimary(string serviceKey);
}
