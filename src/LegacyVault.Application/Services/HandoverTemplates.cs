using System.Text.RegularExpressions;

namespace LegacyVault.Application.Services;

/// <summary>Một đoạn của mẫu biên bản: chữ (có thể chứa trường như {HO_TEN_NGUOI_NHAN}), căn lề và có đậm hay không.</summary>
public class TemplateParagraph
{
    public const string AlignLeft = "left";
    public const string AlignCenter = "center";
    public const string AlignJustify = "justify";

    public string Text { get; set; } = string.Empty;

    /// <summary>"left", "center" hoặc "justify".</summary>
    public string Align { get; set; } = AlignJustify;

    public bool Bold { get; set; }

    public TemplateParagraph()
    {
    }

    public TemplateParagraph(string text, string align, bool bold)
    {
        Text = text;
        Align = align;
        Bold = bold;
    }
}

/// <summary>Nội dung đầy đủ của một bộ mẫu: biên bản đồng ý, văn bản từ chối và câu báo khi có người nhận từ chối.</summary>
public sealed class HandoverTemplateContent
{
    public List<TemplateParagraph> Accept { get; set; } = new List<TemplateParagraph>();

    public List<TemplateParagraph> Decline { get; set; } = new List<TemplateParagraph>();

    /// <summary>Câu báo cách xử lý phần tài sản khi có người nhận từ chối; dùng qua trường {CAU_BAO_TU_CHOI} và hiện cho người nhận trước khi chọn.</summary>
    public string DeclineNotice { get; set; } = string.Empty;
}

/// <summary>Một trường Admin được chèn vào mẫu, kèm nhãn và ví dụ để hiện ở trang sửa mẫu.</summary>
public sealed class TemplateTokenInfo
{
    public string Token { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Example { get; set; } = string.Empty;
}

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

    public static List<TemplateTokenInfo> All()
    {
        List<TemplateTokenInfo> tokens = new List<TemplateTokenInfo>();
        Add(tokens, Day, "Ngày ký (ngày)", "11");
        Add(tokens, Month, "Ngày ký (tháng)", "11");
        Add(tokens, Year, "Ngày ký (năm)", "2026");
        Add(tokens, RecipientName, "Họ tên người nhận", "Trần Minh Anh");
        Add(tokens, RecipientBirthYear, "Năm sinh người nhận", "1990");
        Add(tokens, RecipientCitizenId, "Số căn cước người nhận", "079090001234");
        Add(tokens, OwnerName, "Họ tên Chủ kho (người để lại di sản)", "Nguyễn Văn A");
        Add(tokens, DeathDate, "Ngày mất", "05/11/2026");
        Add(tokens, RegistrationDate, "Ngày đăng ký khai tử", "07/11/2026");
        Add(tokens, RegistrationPlace, "Nơi đăng ký khai tử", "UBND phường Bến Thành, Thành phố Hồ Chí Minh");
        Add(tokens, Inheritance, "Phần di sản (tên gói và số mục)", "Gói gia đình (gồm 3 mục)");
        Add(tokens, DeathSentence, "Cả câu về người mất: ngày mất, nơi và ngày đăng ký khai tử",
            "chết ngày 05/11/2026 theo giấy chứng tử do UBND phường Bến Thành, Thành phố Hồ Chí Minh đăng ký khai tử ngày 07/11/2026.");
        Add(tokens, DeclineNotice, "Câu báo khi có người nhận từ chối", "Nếu một người nhận từ chối nhận, …");
        return tokens;
    }

    private static void Add(List<TemplateTokenInfo> tokens, string token, string label, string example)
    {
        TemplateTokenInfo info = new TemplateTokenInfo();
        info.Token = token;
        info.Label = label;
        info.Example = example;
        tokens.Add(info);
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
        return TokenPattern.Replace(text, match =>
        {
            string name = match.Groups[1].Value;
            string? value;
            if (values.TryGetValue(name, out value))
            {
                return value;
            }
            return match.Value;
        });
    }
}

/// <summary>Bộ mẫu gốc: đúng chữ của biên bản và văn bản từ chối trước khi Admin sửa. Dùng khi chưa có bản nào được lưu.</summary>
public static class HandoverTemplateDefaults
{
    /// <summary>Câu báo mặc định khi có người nhận từ chối.</summary>
    public const string DefaultDeclineNotice =
        "Nếu một người nhận từ chối nhận, phần tài sản của gói bàn giao đó sẽ bị xóa hoàn toàn khỏi hệ thống và không thể khôi phục.";

    public static HandoverTemplateContent Create()
    {
        HandoverTemplateContent content = new HandoverTemplateContent();
        content.DeclineNotice = DefaultDeclineNotice;
        content.Accept = CreateAccept();
        content.Decline = CreateDecline();
        return content;
    }

    private static List<TemplateParagraph> Header(string title)
    {
        List<TemplateParagraph> list = new List<TemplateParagraph>();
        list.Add(new TemplateParagraph("CỘNG HOÀ XÃ HỘI CHỦ NGHĨA VIỆT NAM", TemplateParagraph.AlignCenter, true));
        list.Add(new TemplateParagraph("Độc lập - Tự do - Hạnh phúc", TemplateParagraph.AlignCenter, true));
        list.Add(new TemplateParagraph("-----------------", TemplateParagraph.AlignCenter, false));
        list.Add(new TemplateParagraph(title, TemplateParagraph.AlignCenter, true));
        return list;
    }

    private static List<TemplateParagraph> CreateAccept()
    {
        List<TemplateParagraph> list = Header("BIÊN BẢN ĐỒNG Ý NHẬN DI SẢN THỪA KẾ");
        list.Add(new TemplateParagraph("Hôm nay, ngày {NGAY} tháng {THANG} năm {NAM}, tại hệ thống LegacyVault.", TemplateParagraph.AlignLeft, false));
        list.Add(new TemplateParagraph("Tôi là: {HO_TEN_NGUOI_NHAN}; sinh năm: {NAM_SINH}; Số Căn cước/CCCD: {SO_CCCD}.", TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph("Bằng biên bản này, tôi xin khai đúng sự thật rằng:", TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph(
            "1. Người để lại di sản: Chủ kho {HO_TEN_CHU_KHO}, {CAU_NGUOI_MAT} Thông tin này đã được Người xét duyệt thẩm định trên hệ thống.",
            TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph("2. Tôi là người được Chủ kho chỉ định nhận phần di sản nêu tại mục 4.", TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph(
            "3. Tôi không thuộc các trường hợp không được quyền hưởng di sản theo Điều 621 Bộ luật Dân sự 2015, và trước khi lập biên bản này tôi chưa lập văn bản từ chối nhận di sản đối với phần này.",
            TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph("4. Di sản khai nhận: {DI_SAN}.", TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph(
            "5. Tôi xin cam đoan những điều khai trong biên bản này là đúng sự thật, không khai thiếu, không giấu thông tin. Nếu sai, tôi hoàn toàn chịu trách nhiệm trước pháp luật.",
            TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph(
            "6. Nay, bằng biên bản này tôi đồng ý nhận phần di sản nêu tại mục 4. Tôi hiểu rằng người thừa kế chỉ phải thực hiện nghĩa vụ tài sản của người chết trong phạm vi di sản được nhận theo Điều 615 Bộ luật Dân sự 2015, tự chịu trách nhiệm về việc quản lý, sử dụng tài sản đã nhận và bảo mật mã khóa giải mã dữ liệu được bàn giao.",
            TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph(
            "7. Tôi đã đọc lại toàn bộ nội dung, hiểu rõ, chấp nhận và nhận thức đầy đủ trách nhiệm pháp lý của mình khi lập và xác nhận biên bản này.",
            TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph("8. Tôi đã được thông báo: {CAU_BAO_TU_CHOI}", TemplateParagraph.AlignJustify, false));
        return list;
    }

    private static List<TemplateParagraph> CreateDecline()
    {
        List<TemplateParagraph> list = Header("VĂN BẢN TỪ CHỐI NHẬN DI SẢN THỪA KẾ");
        list.Add(new TemplateParagraph(
            "Hôm nay, ngày {NGAY} tháng {THANG} năm {NAM}, tại hệ thống LegacyVault, tôi là:", TemplateParagraph.AlignLeft, false));
        list.Add(new TemplateParagraph("Ông/bà: {HO_TEN_NGUOI_NHAN} Sinh năm : {NAM_SINH}", TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph("Số Căn cước/CCCD: {SO_CCCD} do Bộ Công an cấp.", TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph("(Là người được chỉ định nhận của người để lại di sản thừa kế)", TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph("   Tôi là người thừa kế của ông/bà {HO_TEN_CHU_KHO}", TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph("   Ông/bà {HO_TEN_CHU_KHO} {CAU_NGUOI_MAT}", TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph("   Di sản mà ông/bà {HO_TEN_CHU_KHO} để lại là: {DI_SAN}.", TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph(
            "Nay bằng Văn bản này tôi tự nguyện từ chối nhận kỷ phần thừa kế di sản nêu trên mà tôi được hưởng.",
            TemplateParagraph.AlignJustify, false));
        list.Add(new TemplateParagraph("          Tôi xin cam đoan:", TemplateParagraph.AlignLeft, true));
        list.Add(new TemplateParagraph(
            "- Những thông tin về nhân thân, về tài sản đã ghi trong văn bản này là đúng sự thật.", TemplateParagraph.AlignLeft, false));
        list.Add(new TemplateParagraph(
            "- Việc từ chối nhận tài sản thừa kế này không nhằm trốn tránh việc thực hiện nghĩa vụ tài sản của mình đối với người khác theo Điều 620 Bộ luật Dân sự 2015.",
            TemplateParagraph.AlignLeft, false));
        list.Add(new TemplateParagraph(
            "- Tôi đã đọc nội dung Văn bản này, đã hiểu rõ trách nhiệm pháp lý của mình khi lập và ký/điểm chỉ vào Văn bản này.",
            TemplateParagraph.AlignLeft, false));
        list.Add(new TemplateParagraph(
            "Văn bản này được gửi đến Người thực thi của kho, là người quản lý di sản, để biết.", TemplateParagraph.AlignJustify, false));
        return list;
    }
}
