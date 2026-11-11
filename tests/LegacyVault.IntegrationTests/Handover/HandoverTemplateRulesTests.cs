using System.Text;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Handover;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Luật thuần của mẫu biên bản: danh sách trường {…}, điền giá trị, mẫu gốc hợp lệ, và bộ dựng ảnh đọc được cả mẫu lẫn chữ cố định cũ.
/// </summary>
public sealed class HandoverTemplateRulesTests
{
    [Fact]
    public void Fill_ReplacesKnownTokens_AndKeepsUnknownOnes()
    {
        Dictionary<string, string> values = new Dictionary<string, string>();
        values[HandoverTemplateTokens.RecipientName] = "Trần Minh Anh";
        values[HandoverTemplateTokens.Year] = "2026";

        string text = HandoverTemplateTokens.Fill("Tôi là {HO_TEN_NGUOI_NHAN}, năm {NAM}, {TRUONG_LA}.", values);

        Assert.Equal("Tôi là Trần Minh Anh, năm 2026, {TRUONG_LA}.", text);
    }

    [Fact]
    public void FindUnknown_ReturnsFirstTokenNotInTheList()
    {
        Assert.Null(HandoverTemplateTokens.FindUnknown("Không có trường nào, hoặc {NGAY} {THANG} {NAM}."));
        Assert.Equal("SAI_TEN", HandoverTemplateTokens.FindUnknown("Có {NGAY} và {SAI_TEN} và {KHAC}."));
    }

    [Fact]
    public void DefaultTemplate_UsesOnlyKnownTokens_AndKeepsTheOriginalWording()
    {
        HandoverTemplateContent content = HandoverTemplateDefaults.Create();

        foreach (TemplateParagraph paragraph in content.Accept.Concat(content.Decline))
        {
            Assert.Null(HandoverTemplateTokens.FindUnknown(paragraph.Text));
            Assert.Contains(paragraph.Align, new[] { "left", "center", "justify" });
        }
        Assert.Contains(content.Accept, p => p.Text.StartsWith("8. Tôi đã được thông báo: {CAU_BAO_TU_CHOI}"));
        Assert.Contains(content.Accept, p => p.Text.Contains("Điều 615 Bộ luật Dân sự 2015"));
        Assert.Contains(content.Decline, p => p.Text.Contains("Điều 620 Bộ luật Dân sự 2015"));
        Assert.Contains("xóa hoàn toàn khỏi hệ thống", content.DeclineNotice);
    }

    [Fact]
    public void Renderer_BuildsFromTemplateParagraphs_AndFromLegacyWording()
    {
        DeclinePaperData data = new DeclinePaperData
        {
            IsAcceptance = true,
            TurnCode = "AB12CD34",
            ContainerName = "Gói gia đình",
            OwnerFullName = "Nguyễn Văn A",
            DeclinerFullName = "Trần Minh Anh",
            CitizenIdLast4 = "1234",
            SubmittedAt = new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero),
        };

        // Chữ cố định cũ (không có mẫu)
        byte[] legacy = new QuestPdfDeclinePaperRenderer().Render(data);
        Assert.Equal("PNG", Encoding.ASCII.GetString(legacy, 1, 3));

        // Dựng theo mẫu gốc và theo một mẫu Admin sửa ngắn gọn
        data.Paragraphs = HandoverTemplateDefaults.Create().Accept;
        byte[] fromDefault = new QuestPdfDeclinePaperRenderer().Render(data);
        Assert.Equal("PNG", Encoding.ASCII.GetString(fromDefault, 1, 3));

        data.Paragraphs = new List<TemplateParagraph>
        {
            new TemplateParagraph("BIÊN BẢN THỬ", "center", true),
            new TemplateParagraph("Tôi là {HO_TEN_NGUOI_NHAN}. {CAU_BAO_TU_CHOI}", "justify", false),
        };
        byte[] custom = new QuestPdfDeclinePaperRenderer().Render(data);
        Assert.Equal("PNG", Encoding.ASCII.GetString(custom, 1, 3));
        Assert.NotEqual(fromDefault.Length, custom.Length);
    }

    [Fact]
    public void PaperData_DeclineNoticeDefaultsToTheOriginalSentence()
    {
        DeclinePaperData data = new DeclinePaperData();

        Assert.Equal(HandoverTemplateDefaults.DefaultDeclineNotice, data.DeclineNotice);
    }
}
