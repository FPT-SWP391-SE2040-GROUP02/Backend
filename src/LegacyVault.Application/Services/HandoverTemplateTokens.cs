using System.Text;
using System.Text.RegularExpressions;

namespace LegacyVault.Application.Services;

/// <summary>Danh sách trường được phép dùng trong mẫu, cách kiểm tra và điền giá trị. Dùng chung cho Backend khi lưu mẫu và dựng ảnh biên bản.</summary>
public static class HandoverTemplateTokens
{
    public const string Day = "NGAY";
    public const string Month = "THANG";
    public const string Year = "NAM";
    public const string RecipientName = "HO_TEN_NGUOI_NHAN";
    public const string RecipientBirthYear = "NAM_SINH";
    public const string RecipientCitizenId = "SO_CCCD";
    public const string OwnerName = "HO_TEN_CHU_KHO";
    public const string DeathDate = "NGAY_MAT";
    public const string RegistrationDate = "NGAY_DANG_KY";
    public const string RegistrationPlace = "NOI_DANG_KY";
    public const string Inheritance = "DI_SAN";
    public const string DeathSentence = "CAU_NGUOI_MAT";
    public const string DeclineNotice = "CAU_BAO_TU_CHOI";

    private static readonly Regex TokenPattern = new Regex(@"\{([A-Za-z_]+)\}", RegexOptions.Compiled);

    /// <summary>Danh sách mọi trường {...} được phép dùng trong mẫu, kèm tên hiển thị và ví dụ cho màn Admin.</summary>
    public static List<TemplateTokenInfo> All()
    {
        List<TemplateTokenInfo> tokens = new List<TemplateTokenInfo>();
        tokens.Add(new TemplateTokenInfo
        {
            Token = Day,
            Label = "Ngày ký (ngày)",
            Example = "11"
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = Month,
            Label = "Ngày ký (tháng)",
            Example = "11"
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = Year,
            Label = "Ngày ký (năm)",
            Example = "2026"
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = RecipientName,
            Label = "Họ tên người nhận",
            Example = "Trần Minh Anh"
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = RecipientBirthYear,
            Label = "Năm sinh người nhận",
            Example = "1990"
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = RecipientCitizenId,
            Label = "Số căn cước người nhận",
            Example = "079090001234"
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = OwnerName,
            Label = "Họ tên Chủ kho (người để lại di sản)",
            Example = "Nguyễn Văn A"
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = DeathDate,
            Label = "Ngày mất",
            Example = "05/11/2026"
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = RegistrationDate,
            Label = "Ngày đăng ký khai tử",
            Example = "07/11/2026"
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = RegistrationPlace,
            Label = "Nơi đăng ký khai tử",
            Example = "UBND phường Bến Thành, Thành phố Hồ Chí Minh"
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = Inheritance,
            Label = "Phần di sản (tên gói và số mục)",
            Example = "Gói gia đình (gồm 3 mục)"
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = DeathSentence,
            Label = "Cả câu về người mất: ngày mất, nơi và ngày đăng ký khai tử",
            Example = "chết ngày 05/11/2026 theo giấy chứng tử do UBND phường Bến Thành, Thành phố Hồ Chí Minh đăng ký khai tử ngày 07/11/2026."
        });
        tokens.Add(new TemplateTokenInfo
        {
            Token = DeclineNotice,
            Label = "Câu báo khi có người nhận từ chối",
            Example = "Nếu một người nhận từ chối nhận, …"
        });
        return tokens;
    }

    /// <summary>Tên trường lạ (không nằm trong danh sách) đầu tiên tìm thấy trong chữ; không có thì null.</summary>
    public static string? FindUnknown(string text)
    {
        HashSet<string> known = new HashSet<string>();
        foreach (TemplateTokenInfo info in All())
        {
            known.Add(info.Token);
        }
        foreach (Match match in TokenPattern.Matches(text))
        {
            string name = match.Groups[1].Value;
            if (!known.Contains(name))
            {
                return name;
            }
        }
        return null;
    }

    /// <summary>Thay từng {TRƯỜNG} bằng giá trị tương ứng; trường không có giá trị thì để nguyên.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        StringBuilder result = new StringBuilder();
        int position = 0;
        foreach (Match match in TokenPattern.Matches(text))
        {
            result.Append(text, position, match.Index - position);
            string name = match.Groups[1].Value;
            string? value;
            if (values.TryGetValue(name, out value))
            {
                result.Append(value);
            }
            else
            {
                result.Append(match.Value);
            }
            position = match.Index + match.Length;
        }
        result.Append(text, position, text.Length - position);
        return result.ToString();
    }
}
