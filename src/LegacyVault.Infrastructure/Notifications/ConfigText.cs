namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Tiện ích làm sạch chuỗi cấu hình.
/// Giá trị dán từ biến môi trường hay bị kèm dấu nháy thừa ở hai đầu.
/// </summary>
public static class ConfigText
{
    /// <summary>
    /// Làm sạch chuỗi cấu hình: trả về chuỗi rỗng khi null,
    /// ngược lại Trim() rồi cắt dấu nháy kép và nháy đơn ở hai đầu theo đúng thứ tự cũ.
    /// </summary>
    public static string Clean(string? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value.Trim().Trim('"').Trim('\'');
    }
}
