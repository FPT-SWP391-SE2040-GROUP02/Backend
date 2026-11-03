using System.Text.Json;

namespace LegacyVault.Application.ServiceConfig;

/// <summary>Đọc/ghi cột SettingsJson của SystemConfigs: một từ điển {tên trường: giá trị chuỗi}.</summary>
public static class SystemConfigJson
{
    /// <summary>Các trường thường của dòng, trường thiếu được điền giá trị mặc định của danh mục.</summary>
    public static Dictionary<string, string> ReadSettings(string serviceKey, string json)
    {
        Dictionary<string, string> settings = SystemConfigCatalog.DefaultSettings(serviceKey);
        Dictionary<string, string>? stored = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (stored != null)
        {
            foreach (KeyValuePair<string, string> entry in stored)
            {
                settings[entry.Key] = entry.Value;
            }
        }
        return settings;
    }

    public static string WriteSettings(IReadOnlyDictionary<string, string> settings)
    {
        return JsonSerializer.Serialize(settings);
    }
}
