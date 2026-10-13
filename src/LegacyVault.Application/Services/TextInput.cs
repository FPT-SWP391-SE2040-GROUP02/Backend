namespace LegacyVault.Application.Services;

/// <summary>Chuẩn hóa chuỗi người dùng nhập.</summary>
public static class TextInput
{
    /// <summary>Trim; rỗng/chỉ khoảng trắng → null.</summary>
    public static string? NullIfBlank(string? value)
    {
        if (value == null)
        {
            return null;
        }
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }
        return trimmed;
    }
}
