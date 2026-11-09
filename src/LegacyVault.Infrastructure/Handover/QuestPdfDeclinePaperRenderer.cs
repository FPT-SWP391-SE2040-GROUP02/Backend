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
        Document document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(12).LineHeight(1.4f));
                page.Content().Column(column =>
                {
                    column.Spacing(10);
                    column.Item().AlignCenter().Text("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM").Bold();
                    column.Item().AlignCenter().Text("Độc lập - Tự do - Hạnh phúc").Bold();
                    column.Item().PaddingTop(10).AlignCenter().Text("VĂN BẢN TỪ CHỐI NHẬN DI SẢN").FontSize(16).Bold();
                    column.Item().AlignCenter().Text($"Mã lượt bàn giao: {data.TurnCode}");
                    column.Item().Text($"Chủ kho (người để lại di sản): {data.OwnerFullName}");
                    column.Item().Text($"Phần di sản từ chối nhận: {data.ContainerName}");
                    column.Item().Text(
                        $"Người từ chối: {data.DeclinerFullName} (số căn cước kết thúc bằng {data.CitizenIdLast4}).");
                    column.Item().Text(
                        "Tôi tự nguyện từ chối nhận phần di sản nêu trên theo Điều 620 Bộ luật Dân sự 2015, "
                        + "việc từ chối không nhằm trốn tránh nghĩa vụ tài sản của người chết.");
                    column.Item().Text($"Thời điểm nộp văn bản: {Format(data.SubmittedAt)}.");
                    column.Item().PaddingTop(10).Text("Kết quả đối chiếu với thông tin Chủ kho đã khai").Bold();
                    column.Item().Text($"Họ tên: {Match(data.NameMatches)}");
                    column.Item().Text($"Số căn cước: {Match(data.CitizenIdMatches)}");
                    column.Item().Text($"Ngày sinh: {Match(data.DateOfBirthMatches)}");
                    column.Item().PaddingTop(10).Text("Xác nhận của Người xét duyệt").Bold();
                    column.Item().Text(
                        $"{data.ReviewerFullName}{card} đã đối chiếu ảnh căn cước và duyệt hồ sơ lúc {Format(data.ReviewedAt)}.");
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
