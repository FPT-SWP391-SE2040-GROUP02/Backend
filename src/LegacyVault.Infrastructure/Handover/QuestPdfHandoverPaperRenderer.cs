using System.Globalization;
using LegacyVault.Application.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Dựng ảnh PNG văn bản từ chối nhận di sản thừa kế và văn bản khai nhận di sản thừa kế bằng QuestPDF (giấy phép Community).
/// Văn bản từ chối viết đúng từng câu theo mẫu Word của nhóm (van-ban-tu-choi-nhan-di-san-thua-ke.docx): khổ Letter, lề 1 inch,
/// font Times New Roman 13pt, màu chữ #222222, khoảng cách đoạn 9pt trước và 6pt sau, căn đều. Máy chủ không có Times New Roman thì dùng
/// Liberation Serif (cùng kích thước chữ), cuối cùng là Lato đi kèm gói.
/// </summary>
public sealed class QuestPdfHandoverPaperRenderer : IHandoverPaperRenderer
{
    /// <summary>Kiểu in của một đoạn: căn trái, giữa hoặc đều; thêm Bold là chữ đậm.</summary>
    private enum ParaStyle
    {
        Left,
        LeftBold,
        Center,
        CenterBold,
        Justify,
        JustifyBold
    }

    private const string Ink = "#222222";

    static QuestPdfHandoverPaperRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Times New Roman là font của Windows nên không nhúng vào gói: dùng font hệ thống nếu máy có (hoặc Liberation Serif trên Linux),
        // không có thì QuestPDF dùng Lato đi kèm thay vì dừng dựng văn bản.
        QuestPDF.Settings.UseSystemFonts = true;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
    }

    /// <summary>Dựng văn bản thành ảnh PNG một trang. Có mẫu của Admin (data.Paragraphs) thì in theo mẫu, không thì in chữ cố định.</summary>
    public byte[] Render(HandoverPaperData data)
    {
        Document document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(54);
                page.DefaultTextStyle(style => style
                    .FontFamily("Times New Roman", "Liberation Serif", "Lato")
                    .FontSize(11.5f)
                    .FontColor(Ink)
                    .LineHeight(1.0f));
                page.Content().Column(column =>
                {
                    if (data.Paragraphs != null)
                    {
                        WriteFromTemplate(column, data);
                    }
                    else if (data.IsAcceptance)
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
        ImageGenerationSettings settings = new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            RasterDpi = 150
        };
        // Văn bản chỉ một trang: lấy trang đầu làm ảnh PNG, xem như ảnh giấy chứng tử mà Người xét duyệt đọc.
        byte[] firstPage = new byte[0];
        foreach (byte[] page in document.GenerateImages(settings))
        {
            firstPage = page;
            break;
        }
        return firstPage;
    }

    /// <summary>
    /// Dựng theo mẫu Admin đã lưu (hoặc mẫu gốc): mỗi đoạn điền các trường {…} từ dữ liệu hệ thống. Khối chữ ký hoặc dòng ký tên ở cuối
    /// là phần cố định, không nằm trong mẫu.
    /// </summary>
    private static void WriteFromTemplate(ColumnDescriptor column, HandoverPaperData data)
    {
        Dictionary<string, string> values = BuildValues(data);
        foreach (TemplateParagraph paragraph in data.Paragraphs!)
        {
            string text = HandoverTemplateTokens.Fill(paragraph.Text, values);
            Para(column, text, StyleOf(paragraph));
        }
        if (data.HasSignatures)
        {
            WriteSignatures(column, data);
        }
        else
        {
            if (data.IsAcceptance)
            {
                Para(column, "Người lập Biên bản đồng ý nhận di sản thừa kế", ParaStyle.CenterBold);
            }
            else
            {
                Para(column, "Người lập Văn bản từ chối nhận di sản thừa kế", ParaStyle.CenterBold);
            }
            Para(column, "(Ký/điểm chỉ và ghi rõ họ tên)", ParaStyle.Center);
        }
    }

    /// <summary>Giá trị cho từng trường của mẫu; thiếu dữ liệu thì in dòng chấm như mẫu giấy.</summary>
    private static Dictionary<string, string> BuildValues(HandoverPaperData data)
    {
        DateTimeOffset local = data.SubmittedAt.ToOffset(TimeSpan.FromHours(7));
        string place = "UBND …………………";
        if (!string.IsNullOrWhiteSpace(data.DeathRegistrationPlace))
        {
            place = data.DeathRegistrationPlace;
        }
        Dictionary<string, string> values = new Dictionary<string, string>();
        values[HandoverTemplateTokens.Day] = local.Day.ToString("00");
        values[HandoverTemplateTokens.Month] = local.Month.ToString("00");
        values[HandoverTemplateTokens.Year] = local.Year.ToString();
        values[HandoverTemplateTokens.RecipientName] = data.RecipientFullName;
        values[HandoverTemplateTokens.RecipientBirthYear] = BirthYearText(data);
        values[HandoverTemplateTokens.RecipientCitizenId] = CitizenIdText(data);
        values[HandoverTemplateTokens.OwnerName] = data.OwnerFullName;
        values[HandoverTemplateTokens.DeathDate] = Date(data.DateOfDeath, "……………");
        values[HandoverTemplateTokens.RegistrationDate] = Date(data.DeathCertificateIssuedOn, "…………………………");
        values[HandoverTemplateTokens.RegistrationPlace] = place;
        values[HandoverTemplateTokens.Inheritance] = ContainerText(data);
        values[HandoverTemplateTokens.DeathSentence] = DeathSentence(data);
        values[HandoverTemplateTokens.DeclineNotice] = data.DeclineNotice;
        return values;
    }

    /// <summary>Văn bản từ chối: viết theo thể thức văn bản pháp lý, điền dữ liệu hệ thống đã giữ, đồng nhất xưng Tôi.</summary>
    private static void WriteDecline(ColumnDescriptor column, HandoverPaperData data)
    {
        DateTimeOffset local = data.SubmittedAt.ToOffset(TimeSpan.FromHours(7));
        string nbsp3 = "   ";
        Para(column, "CỘNG HOÀ XÃ HỘI CHỦ NGHĨA VIỆT NAM", ParaStyle.CenterBold);
        Para(column, "Độc lập - Tự do - Hạnh phúc", ParaStyle.CenterBold);
        Para(column, "-----------------", ParaStyle.Center);
        Para(column, "VĂN BẢN TỪ CHỐI NHẬN DI SẢN THỪA KẾ", ParaStyle.CenterBold);
        Para(column,
            $"Hôm nay, ngày {local.Day:00} tháng {local.Month:00} năm {local.Year}, tại hệ thống LegacyVault, tôi là:");
        Para(column, $"Ông/bà: {data.RecipientFullName} Sinh năm : {BirthYearText(data)}", ParaStyle.Justify);
        Para(column,
            $"Số Căn cước/CCCD: {CitizenIdText(data)} do Bộ Công an cấp.", ParaStyle.Justify);
        Para(column, "(Là người được chỉ định nhận của người để lại di sản thừa kế)", ParaStyle.Justify);
        Para(column, $"{nbsp3}Tôi là người thừa kế của ông/bà {data.OwnerFullName}", ParaStyle.Justify);
        Para(column,
            $"{nbsp3} Ông/bà {data.OwnerFullName} {DeathSentence(data)}", ParaStyle.Justify);
        Para(column, $"{nbsp3}Di sản mà ông/bà {data.OwnerFullName} để lại là: {ContainerText(data)}.", ParaStyle.Justify);
        Para(column,
            "Nay bằng Văn bản này tôi tự nguyện từ chối nhận kỷ phần thừa kế di sản nêu trên mà tôi được hưởng.", ParaStyle.Justify);
        Para(column, "          Tôi xin cam đoan:", ParaStyle.LeftBold);
        Para(column, "- Những thông tin về nhân thân, về tài sản đã ghi trong văn bản này là đúng sự thật.");
        Para(column,
            "- Việc từ chối nhận tài sản thừa kế này không nhằm trốn tránh việc thực hiện nghĩa vụ tài sản của mình đối với người khác theo Điều 620 Bộ luật Dân sự 2015.");
        Para(column,
            "- Tôi đã đọc nội dung Văn bản này, đã hiểu rõ trách nhiệm pháp lý của mình khi lập và ký/điểm chỉ vào Văn bản này.");
        if (data.HasSignatures)
        {
            Para(column, "Văn bản này được gửi đến Người thực thi của kho, là người quản lý di sản, để biết.", ParaStyle.Justify);
            WriteSignatures(column, data);
        }
        else
        {
            Para(column, "Người lập Văn bản từ chối nhận di sản thừa kế", ParaStyle.CenterBold);
            Para(column, "(Ký/điểm chỉ và ghi rõ họ tên)", ParaStyle.Center);
        }
    }

    /// <summary>Biên bản đồng ý nhận di sản thừa kế, cùng khổ giấy và kiểu chữ với văn bản từ chối.</summary>
    private static void WriteAcceptance(ColumnDescriptor column, HandoverPaperData data)
    {
        DateTimeOffset local = data.SubmittedAt.ToOffset(TimeSpan.FromHours(7));
        Para(column, "CỘNG HOÀ XÃ HỘI CHỦ NGHĨA VIỆT NAM", ParaStyle.CenterBold);
        Para(column, "Độc lập - Tự do - Hạnh phúc", ParaStyle.CenterBold);
        Para(column, "-----------------", ParaStyle.Center);
        Para(column, "BIÊN BẢN ĐỒNG Ý NHẬN DI SẢN THỪA KẾ", ParaStyle.CenterBold);
        Para(column, $"Hôm nay, ngày {local.Day:00} tháng {local.Month:00} năm {local.Year}, tại hệ thống LegacyVault.");
        Para(column, $"Tôi là: {data.RecipientFullName}; sinh năm: {BirthYearText(data)}; Số Căn cước/CCCD: {CitizenIdText(data)}.", ParaStyle.Justify);
        Para(column, "Bằng biên bản này, tôi xin khai đúng sự thật rằng:", ParaStyle.Justify);
        Para(column,
            $"1. Người để lại di sản: Chủ kho {data.OwnerFullName}, {DeathSentence(data)} "
            + "Thông tin này đã được Người xét duyệt thẩm định trên hệ thống.", ParaStyle.Justify);
        Para(column, "2. Tôi là người được Chủ kho chỉ định nhận phần di sản nêu tại mục 4.", ParaStyle.Justify);
        Para(column,
            "3. Tôi không thuộc các trường hợp không được quyền hưởng di sản theo Điều 621 Bộ luật Dân sự 2015, và trước khi lập biên bản này tôi chưa lập văn bản "
            + "từ chối nhận di sản đối với phần này.", ParaStyle.Justify);
        Para(column, $"4. Di sản khai nhận: {ContainerText(data)}.", ParaStyle.Justify);
        Para(column,
            "5. Tôi xin cam đoan những điều khai trong biên bản này là đúng sự thật, không khai thiếu, không giấu thông tin. "
            + "Nếu sai, tôi hoàn toàn chịu trách nhiệm trước pháp luật.", ParaStyle.Justify);
        Para(column,
            "6. Nay, bằng biên bản này tôi đồng ý nhận phần di sản nêu tại mục 4. Tôi hiểu rằng người thừa kế chỉ phải thực hiện "
            + "nghĩa vụ tài sản của người chết trong phạm vi di sản được nhận theo Điều 615 Bộ luật Dân sự 2015, tự chịu trách nhiệm "
            + "về việc quản lý, sử dụng tài sản đã nhận và bảo mật mã khóa giải mã dữ liệu được bàn giao.", ParaStyle.Justify);
        Para(column,
            "7. Tôi đã đọc lại toàn bộ nội dung, hiểu rõ, chấp nhận và nhận thức đầy đủ trách nhiệm pháp lý của mình khi lập và "
            + "xác nhận biên bản này.", ParaStyle.Justify);
        Para(column, $"8. Tôi đã được thông báo: {data.DeclineNotice}", ParaStyle.Justify);
        if (data.HasSignatures)
        {
            WriteSignatures(column, data);
        }
        else
        {
            Para(column, "Người lập Biên bản đồng ý nhận di sản thừa kế", ParaStyle.CenterBold);
            Para(column, "(Ký/điểm chỉ và ghi rõ họ tên)", ParaStyle.Center);
        }
    }

    /// <summary>
    /// Câu về người mất, dùng chung cho văn bản từ chối và biên bản đồng ý:
    /// "chết ngày {ngày mất} theo giấy chứng tử do {nơi đăng ký} đăng ký khai tử ngày {ngày đăng ký}."
    /// Thiếu dữ liệu nào thì in dòng chấm ở chỗ đó. Không in số giấy chứng tử.
    /// </summary>
    public static string DeathSentence(HandoverPaperData data)
    {
        string place = "UBND …………………";
        if (!string.IsNullOrWhiteSpace(data.DeathRegistrationPlace))
        {
            place = data.DeathRegistrationPlace;
        }
        return $"chết ngày {Date(data.DateOfDeath, "……………")} theo giấy chứng tử do {place} "
            + $"đăng ký khai tử ngày {Date(data.DeathCertificateIssuedOn, "…………………………")}.";
    }

    /// <summary>Có năm sinh người nhận khai thì in năm; không thì để dòng chấm như mẫu cũ.</summary>
    private static string BirthYearText(HandoverPaperData data)
    {
        if (data.RecipientBirthYear != null)
        {
            return data.RecipientBirthYear.Value.ToString();
        }
        return "...................................................";
    }

    /// <summary>In số căn cước 12 số khi có trong hồ sơ người nhận, hoặc số đã lưu khi chỉ còn hậu tố.</summary>
    private static string CitizenIdText(HandoverPaperData data)
    {
        if (!string.IsNullOrWhiteSpace(data.CitizenId))
        {
            return data.CitizenId;
        }
        if (!string.IsNullOrWhiteSpace(data.CitizenIdLast4))
        {
            return data.CitizenIdLast4;
        }
        return "............";
    }

    /// <summary>Tên gói kèm số mục (nếu có) để in ở phần di sản.</summary>
    private static string ContainerText(HandoverPaperData data)
    {
        if (data.ItemCount > 0)
        {
            return $"{data.ContainerName} (gồm {data.ItemCount} mục)";
        }
        return data.ContainerName;
    }

    /// <summary>Khối chữ ký của hai bên: người lập và Người xét duyệt đã kiểm tra thủ công. Thông tin về Chủ kho chép từ hồ sơ chứng tử đã duyệt nên không có chữ ký riêng.</summary>
    private static void WriteSignatures(ColumnDescriptor column, HandoverPaperData data)
    {
        column.Item().PaddingTop(12).Row(row =>
        {
            SignCell(row.RelativeItem(), data, true);
            SignCell(row.RelativeItem(), data, false);
        });
    }

    /// <summary>Một ô chữ ký: tiêu đề, họ tên và dòng ký điện tử (hoặc "chưa ký"). forRecipient là true thì ô của người nhận, ngược lại là ô của Người xét duyệt.</summary>
    private static void SignCell(IContainer cell, HandoverPaperData data, bool forRecipient)
    {
        string title = "Người xét duyệt đã kiểm tra thủ công";
        string name = data.ReviewerFullName;
        DateTimeOffset? signedAt = data.ReviewedAt;
        if (forRecipient)
        {
            title = "Người lập văn bản";
            name = data.RecipientFullName;
            signedAt = data.RecipientSignedAt;
        }
        cell.PaddingHorizontal(4).Column(c =>
        {
            c.Item().AlignCenter().Text(title).Bold().FontSize(10);
            c.Item().PaddingTop(4).AlignCenter().Text(name);
            string stamp = "(chưa ký)";
            if (signedAt != null)
            {
                DateTimeOffset at = signedAt.Value.ToOffset(TimeSpan.FromHours(7));
                stamp = "Đã ký điện tử " + at.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
            }
            c.Item().AlignCenter().Text(stamp).FontSize(9.5f);
        });
    }

    /// <summary>Kiểu in của một đoạn mẫu: căn trái, giữa hoặc đều, có đậm hoặc không.</summary>
    private static ParaStyle StyleOf(TemplateParagraph paragraph)
    {
        if (paragraph.Align == TemplateParagraph.AlignCenter)
        {
            if (paragraph.Bold)
            {
                return ParaStyle.CenterBold;
            }
            return ParaStyle.Center;
        }
        if (paragraph.Align == TemplateParagraph.AlignJustify)
        {
            if (paragraph.Bold)
            {
                return ParaStyle.JustifyBold;
            }
            return ParaStyle.Justify;
        }
        if (paragraph.Bold)
        {
            return ParaStyle.LeftBold;
        }
        return ParaStyle.Left;
    }

    /// <summary>Một đoạn theo mẫu Word: cách đoạn trên 9pt, dưới 6pt; tiêu đề căn giữa và đậm, thân văn bản căn đều.</summary>
    private static void Para(ColumnDescriptor column, string text, ParaStyle style = ParaStyle.Left)
    {
        bool center = style == ParaStyle.Center || style == ParaStyle.CenterBold;
        bool justify = style == ParaStyle.Justify || style == ParaStyle.JustifyBold;
        bool bold = style == ParaStyle.LeftBold || style == ParaStyle.CenterBold || style == ParaStyle.JustifyBold;
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

    /// <summary>Ngày dạng dd/MM/yyyy; chưa có ngày thì in chuỗi chấm blank như mẫu giấy.</summary>
    private static string Date(DateOnly? value, string blank)
    {
        if (value == null)
        {
            return blank;
        }
        return value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }
}
