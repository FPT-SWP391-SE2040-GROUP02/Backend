using LegacyVault.Application.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Dựng ảnh PNG văn bản từ chối nhận di sản thừa kế và văn bản khai nhận di sản thừa kế bằng QuestPDF (giấy phép Community).
/// Văn bản từ chối viết đúng từng câu theo mẫu Word của nhóm (van-ban-tu-choi-nhan-di-san-thua-ke.docx): khổ Letter, lề 1 inch,
/// font Calibri 13pt, màu chữ #222222, khoảng cách đoạn 9pt trước và 6pt sau, căn đều. Máy chủ không có Calibri thì dùng
/// Carlito (cùng kích thước chữ), cuối cùng là Lato đi kèm gói.
/// </summary>
public sealed class QuestPdfDeclinePaperRenderer : IDeclinePaperRenderer
{
    private const string Ink = "#222222";

    static QuestPdfDeclinePaperRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Calibri là font của Windows nên không nhúng vào gói: dùng font hệ thống nếu máy có (Calibri, hoặc Carlito trên Linux),
        // không có thì QuestPDF dùng Lato đi kèm thay vì dừng dựng văn bản.
        QuestPDF.Settings.UseSystemFonts = true;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
    }

    public byte[] Render(DeclinePaperData data)
    {
        Document document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(72);
                page.DefaultTextStyle(style => style
                    .FontFamily("Calibri", "Carlito", "Lato")
                    .FontSize(13)
                    .FontColor(Ink)
                    .LineHeight(1.0f));
                page.Content().Column(column =>
                {
                    if (data.IsAcceptance)
                    {
                        WriteAcceptance(column, data);
                    }
                    else
                    {
                        WriteDecline(column, data);
                    }
                });
            });
        });
        ImageGenerationSettings settings = new ImageGenerationSettings();
        settings.ImageFormat = ImageFormat.Png;
        settings.RasterDpi = 150;
        // Văn bản chỉ một trang: lấy trang đầu làm ảnh PNG, xem như ảnh giấy chứng tử mà Người xét duyệt đọc.
        return document.GenerateImages(settings).First();
    }

    /// <summary>Văn bản từ chối: giữ nguyên từng câu của mẫu Word, chỗ nào hệ thống có dữ liệu thì điền, còn lại để dấu chấm.</summary>
    private static void WriteDecline(ColumnDescriptor column, DeclinePaperData data)
    {
        DateTimeOffset local = data.SubmittedAt.ToOffset(TimeSpan.FromHours(7));
        string nbsp3 = "   ";
        Para(column, "CỘNG HOÀ XÃ HỘI CHỦ NGHĨA VIỆT NAM", center: true, bold: true);
        Para(column, "Độc lập - Tự do - Hạnh phúc", center: true, bold: true);
        Para(column, "-----------------", center: true);
        Para(column, "VĂN BẢN TỪ CHỐI NHẬN DI SẢN THỪA KẾ", center: true, bold: true);
        Para(column,
            $"Hôm nay, ngày {local.Day:00} tháng {local.Month:00} năm {local.Year}, tại (1) hệ thống LegacyVault, chúng tôi gồm: (2)");
        Para(column, $"1. Ông/bà: {data.DeclinerFullName} Sinh năm : ...................................................", justify: true);
        Para(column,
            "CMND số: ............. do Công an ............... cấp ngày .................................................",
            justify: true);
        Para(column, "(Là (3) người được chỉ định nhận của người để lại di sản thừa kế)", justify: true);
        Para(column, $"{nbsp3} Chúng tôi là những người thừa kế của ông/bà {data.OwnerFullName}", justify: true);
        Para(column,
            $"{nbsp3}  Ông/bà (4) {data.OwnerFullName} chết ngày {Date(data.DateOfDeath, "……………")} theo giấy chứng tử "
            + $"do UBND ………………… đăng ký khai tử ngày {Date(data.DeathCertificateIssuedOn, "…………………………")}.",
            justify: true);
        Para(column, $"{nbsp3} Di sản mà ông/bà {data.OwnerFullName} để lại là: (5) {data.ContainerName}", justify: true);
        Para(column,
            "Nay bằng Văn bản này chúng tôi tự nguyện từ chối nhận kỷ phần thừa kế di sản nêu trên mà chúng tôi được hưởng.",
            justify: true);
        Para(column, "          Chúng tôi xin cam đoan:", bold: true);
        Para(column, "- Những thông tin về nhân thân, về tài sản đã ghi trong văn bản này là đúng sự thật.");
        Para(column,
            "- Việc từ chối nhận tài sản thừa kế này không nhằm trốn tránh việc thực hiện nghĩa vụ tài sản của mình đối với người khác.");
        Para(column,
            "- Chúng tôi đã đọc nội dung Văn bản này, đã hiểu rõ trách nhiệm pháp lý của mình khi lập và ký/điểm chỉ vào Văn bản này.");
        Para(column, "Người lập Văn bản từ chối nhận di sản thừa kế", center: true, bold: true);
        Para(column, "(Ký/điểm chỉ và ghi rõ họ tên)", center: true);
    }

    /// <summary>Văn bản khai nhận di sản thừa kế, cùng khổ giấy và kiểu chữ với văn bản từ chối.</summary>
    private static void WriteAcceptance(ColumnDescriptor column, DeclinePaperData data)
    {
        DateTimeOffset local = data.SubmittedAt.ToOffset(TimeSpan.FromHours(7));
        Para(column, "CỘNG HOÀ XÃ HỘI CHỦ NGHĨA VIỆT NAM", center: true, bold: true);
        Para(column, "Độc lập - Tự do - Hạnh phúc", center: true, bold: true);
        Para(column, "-----------------", center: true);
        Para(column, "BIÊN BẢN ĐỒNG Ý NHẬN DI SẢN THỪA KẾ", center: true, bold: true);
        Para(column, $"Hôm nay, ngày {local.Day:00} tháng {local.Month:00} năm {local.Year}, tại hệ thống LegacyVault.");
        Para(column, $"Tôi là: {data.DeclinerFullName}.", justify: true);
        Para(column, "Bằng biên bản này, tôi xin khai đúng sự thật rằng:", justify: true);
        Para(column,
            $"1. Người để lại di sản: Chủ kho {data.OwnerFullName}, chết ngày {Date(data.DateOfDeath, "……………")} theo giấy "
            + $"chứng tử đã được Người xét duyệt duyệt trong hệ thống, đăng ký khai tử ngày {Date(data.DeathCertificateIssuedOn, "…………………")}.",
            justify: true);
        Para(column, "2. Tôi là người được Chủ kho chỉ định nhận phần di sản nêu tại mục 4.", justify: true);
        Para(column,
            "3. Tôi không thuộc các trường hợp không được quyền hưởng di sản, và trước khi lập biên bản này tôi chưa lập văn bản "
            + "từ chối nhận di sản đối với phần này.",
            justify: true);
        Para(column, $"4. Di sản khai nhận: {data.ContainerName}.", justify: true);
        Para(column,
            "5. Tôi xin cam đoan những điều khai trong biên bản này là đúng sự thật, không khai thiếu, không giấu thông tin. "
            + "Nếu sai, tôi hoàn toàn chịu trách nhiệm trước pháp luật.",
            justify: true);
        Para(column,
            "6. Nay, bằng biên bản này tôi đồng ý nhận phần di sản nêu tại mục 4. Tôi hiểu rằng người thừa kế chỉ phải thực hiện "
            + "nghĩa vụ tài sản của người chết trong phạm vi di sản được nhận, tự chịu trách nhiệm về việc quản lý, sử dụng tài sản "
            + "đã nhận và giữ kín khóa mở gói.",
            justify: true);
        Para(column,
            "7. Tôi đã đọc lại toàn bộ nội dung, hiểu rõ, chấp nhận và nhận thức đầy đủ trách nhiệm pháp lý của mình khi lập và "
            + "xác nhận biên bản này.",
            justify: true);
        Para(column, "Người lập Biên bản đồng ý nhận di sản thừa kế", center: true, bold: true);
        Para(column, "(Ký/điểm chỉ và ghi rõ họ tên)", center: true);
    }

    /// <summary>Một đoạn theo mẫu Word: cách đoạn trên 9pt, dưới 6pt; tiêu đề căn giữa và đậm, thân văn bản căn đều.</summary>
    private static void Para(ColumnDescriptor column, string text, bool center = false, bool bold = false, bool justify = false)
    {
        IContainer item = column.Item().PaddingTop(9).PaddingBottom(6);
        item.Text(t =>
        {
            if (center)
            {
                t.AlignCenter();
            }
            else if (justify)
            {
                t.Justify();
            }
            TextSpanDescriptor span = t.Span(text);
            if (bold)
            {
                span.Bold();
            }
        });
    }

    private static string Date(DateOnly? value, string blank)
    {
        return value == null ? blank : value.Value.ToString("dd/MM/yyyy");
    }
}
