namespace LegacyVault.Application.Services;

/// <summary>Bộ mẫu gốc: đúng chữ của biên bản và văn bản từ chối trước khi Admin sửa. Dùng khi chưa có bản nào được lưu.</summary>
public static class HandoverTemplateDefaults
{
    /// <summary>Câu báo mặc định khi có người nhận từ chối.</summary>
    public const string DefaultDeclineNotice =
        "Nếu một người nhận từ chối nhận, phần tài sản của gói bàn giao đó sẽ bị xóa hoàn toàn khỏi hệ thống và không thể khôi phục.";

    /// <summary>Bộ mẫu gốc gồm biên bản đồng ý, văn bản từ chối và câu báo từ chối.</summary>
    public static HandoverTemplateContent Create()
    {
        HandoverTemplateContent content = new HandoverTemplateContent
        {
            DeclineNotice = DefaultDeclineNotice,
            Accept = CreateAccept(),
            Decline = CreateDecline()
        };
        return content;
    }

    /// <summary>Bốn dòng đầu của mọi văn bản: quốc hiệu, tiêu ngữ, đường kẻ và tiêu đề.</summary>
    private static List<TemplateParagraph> Header(string title)
    {
        List<TemplateParagraph> list = new List<TemplateParagraph>();
        list.Add(new TemplateParagraph("CỘNG HOÀ XÃ HỘI CHỦ NGHĨA VIỆT NAM", TemplateParagraph.AlignCenter, true));
        list.Add(new TemplateParagraph("Độc lập - Tự do - Hạnh phúc", TemplateParagraph.AlignCenter, true));
        list.Add(new TemplateParagraph("-----------------", TemplateParagraph.AlignCenter, false));
        list.Add(new TemplateParagraph(title, TemplateParagraph.AlignCenter, true));
        return list;
    }

    /// <summary>Các đoạn chữ gốc của biên bản đồng ý nhận.</summary>
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

    /// <summary>Các đoạn chữ gốc của văn bản từ chối nhận.</summary>
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
