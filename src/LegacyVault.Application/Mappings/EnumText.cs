using System.Text;

namespace LegacyVault.Application.Mappings;

/// <summary>Đổi tên enum C# sang chuỗi UPPER_SNAKE_CASE của contract API: PaidActive → "PAID_ACTIVE".</summary>
public static class EnumText
{
    public static string ToApiString<TEnum>(this TEnum value) where TEnum : struct, Enum
    {
        return ToUpperSnake(value.ToString());
    }

    public static string ToUpperSnake(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                builder.Append('_');
            }
            builder.Append(char.ToUpperInvariant(name[i]));
        }
        return builder.ToString();
    }
}
