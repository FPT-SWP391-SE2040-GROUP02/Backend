namespace LegacyVault.Application.Notifications;

/// <summary>Chữ gốc (trong mã) của từng loại thư, dựng bằng dữ liệu mẫu để Admin xem và sửa tiếp từ đó.</summary>
public interface IEmailTemplateDefaults
{
    /// <summary>Chữ gốc của loại thư; khóa lạ thì null. Giá trị đã thay biến bằng dữ liệu mẫu.</summary>
    EmailParts? Find(string key);
}
