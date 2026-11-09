using LegacyVault.Application.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Dựng ảnh PNG văn bản từ chối bằng QuestPDF (giấy phép Community); dùng font Lato đi kèm gói nên máy chủ nào cũng dựng như nhau.</summary>
public sealed class QuestPdfDeclinePaperRenderer : IDeclinePaperRenderer
{
    static QuestPdfDeclinePaperRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(DeclinePaperData data)
    {
        string card = string.IsNullOrWhiteSpace(data.ReviewerLawyerCardNumber)
            ? string.Empty
            : $", thẻ luật sư {data.ReviewerLawyerCardNumber!.Trim()}";
        DateTimeOffset local = data.SubmittedAt.ToOffset(TimeSpan.FromHours(7));
        string opening = $"Hôm nay, ngày {local.Day:00} tháng {local.Month:00} năm {local.Year}, tại hệ thống LegacyVault.";
        string person = $"{data.DeclinerFullName}, số căn cước kết thúc bằng {data.CitizenIdLast4}";
        string deceased = $"Chủ kho {data.OwnerFullName}, đã chết theo giấy chứng tử đã được Người xét duyệt duyệt trong hệ thống";
        Document document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(11).LineHeight(1.35f));
                page.Content().Column(column =>
                {
                    column.Spacing(6);
                    column.Item().AlignCenter().Text("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM").Bold();
                    column.Item().AlignCenter().Text("Độc lập - Tự do - Hạnh phúc").Bold();
                    column.Item().PaddingTop(8).AlignCenter().Text(
                        data.IsAcceptance ? "VĂN BẢN KHAI NHẬN DI SẢN THỪA KẾ" : "VĂN BẢN TỪ CHỐI NHẬN DI SẢN THỪA KẾ")
                        .FontSize(15).Bold();
                    column.Item().AlignCenter().Text($"Mã lượt bàn giao: {data.TurnCode}");
                    column.Item().Text(opening);
                    column.Item().Text(data.IsAcceptance ? "Tôi là người khai nhận:" : "Tôi là người từ chối:").Bold();
                    column.Item().Text(person + ".");
                    if (data.IsAcceptance)
                    {
                        column.Item().Text("Bằng văn bản này, tôi xin khai đúng sự thật rằng:");
                        column.Item().Text($"1. Người để lại di sản: {deceased}.");
                        column.Item().Text("2. Tôi là người được Chủ kho chỉ định nhận phần di sản nêu tại mục 4.");
                        column.Item().Text(
                            "3. Tôi không thuộc các trường hợp không được quyền hưởng di sản theo Điều 621 Bộ luật Dân sự 2015, "
                            + "và trước khi lập văn bản này tôi chưa lập văn bản từ chối nhận di sản đối với phần này.");
                        column.Item().Text($"4. Di sản khai nhận: {data.ContainerName}.");
                        column.Item().Text(
                            "5. Tôi xin cam đoan những điều khai trong văn bản này là đúng sự thật, không khai thiếu, không giấu "
                            + "thông tin. Nếu sai, tôi hoàn toàn chịu trách nhiệm trước pháp luật.");
                        column.Item().Text(
                            "6. Nay, bằng văn bản này tôi đồng ý nhận phần di sản nêu tại mục 4. Tôi hiểu rằng người thừa kế chỉ "
                            + "phải thực hiện nghĩa vụ tài sản của người chết trong phạm vi di sản được nhận (Điều 615 Bộ luật Dân sự "
                            + "2015), tự chịu trách nhiệm về việc quản lý, sử dụng tài sản đã nhận và giữ kín khóa mở gói.");
                        column.Item().Text(
                            "7. Tôi đã đọc lại toàn bộ nội dung, hiểu rõ, chấp nhận và nhận thức đầy đủ trách nhiệm pháp lý của "
                            + "mình khi lập và xác nhận văn bản này.");
                    }
                    else
                    {
                        column.Item().Text($"Là người được chỉ định nhận di sản của: {deceased}.");
                        column.Item().Text($"Di sản mà tôi từ chối nhận là: {data.ContainerName}.");
                        column.Item().Text(
                            "Nay bằng văn bản này, tôi tự nguyện từ chối nhận phần di sản nêu trên theo Điều 620 Bộ luật Dân sự "
                            + "2015. Văn bản được lập trước thời điểm phân chia di sản.");
                        column.Item().Text("Tôi xin cam đoan:").Bold();
                        column.Item().Text("- Những thông tin về nhân thân, về tài sản đã ghi trong văn bản này là đúng sự thật.");
                        column.Item().Text(
                            "- Việc từ chối nhận di sản này không nhằm trốn tránh việc thực hiện nghĩa vụ tài sản của tôi đối "
                            + "với người khác.");
                        column.Item().Text(
                            "- Tôi đã đọc nội dung văn bản này, hiểu rõ trách nhiệm pháp lý của mình khi lập và xác nhận văn bản này.");
                    }
                    column.Item().AlignRight().Text(
                        data.IsAcceptance ? "Người khai nhận di sản thừa kế" : "Người lập văn bản từ chối nhận di sản thừa kế").Bold();
                    column.Item().AlignRight().Text($"{data.DeclinerFullName} (xác nhận điện tử lúc {Format(data.SubmittedAt)})");
                    column.Item().PaddingTop(4).Text("Kết quả đối chiếu với thông tin Chủ kho đã khai").Bold();
                    column.Item().Text(
                        $"Họ tên: {Match(data.NameMatches)}; số căn cước: {Match(data.CitizenIdMatches)}; "
                        + $"ngày sinh: {Match(data.DateOfBirthMatches)}.");
                    column.Item().Text("Xác nhận của Người xét duyệt (người làm chứng)").Bold();
                    column.Item().Text(
                        $"{data.ReviewerFullName}{card} đã đối chiếu ảnh căn cước và duyệt hồ sơ lúc {Format(data.ReviewedAt)}.");
                    column.Item().Text(
                        (data.IsAcceptance
                            ? "Căn cứ tham khảo: Điều 613, 614, 615, 621 Bộ luật Dân sự 2015 (Luật số 91/2015/QH13)."
                            : "Căn cứ tham khảo: Điều 620 Bộ luật Dân sự 2015 (Luật số 91/2015/QH13); công chứng văn bản từ chối "
                              + "theo Điều 60 Luật Công chứng 2024 (Luật số 46/2024/QH15) khi cần.")).FontSize(9);
                });
            });
        });
        ImageGenerationSettings settings = new ImageGenerationSettings();
        settings.ImageFormat = ImageFormat.Png;
        settings.RasterDpi = 150;
        // Văn bản chỉ một trang: lấy trang đầu làm ảnh PNG, xem như ảnh giấy chứng tử mà Người xét duyệt đọc.
        return document.GenerateImages(settings).First();
    }

    private static string Match(bool value)
    {
        return value ? "khớp" : "không khớp";
    }

    private static string Format(DateTimeOffset value)
    {
        return value.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm 'ngày' dd/MM/yyyy") + " (giờ Việt Nam)";
    }
}
