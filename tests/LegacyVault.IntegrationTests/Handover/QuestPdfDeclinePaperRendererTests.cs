using System.Text;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Handover;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Văn bản từ chối: dựng được ảnh PNG hợp lệ từ dữ liệu hệ thống đã giữ.</summary>
public sealed class QuestPdfDeclinePaperRendererTests
{
    [Fact]
    public void Render_ProducesPngBytes()
    {
        DeclinePaperData data = new DeclinePaperData
        {
            TurnCode = "AB12CD34",
            ContainerName = "Gói gia đình",
            OwnerFullName = "Nguyễn Văn A",
            DeclinerFullName = "Trần Minh Anh",
            CitizenIdLast4 = "1234",
            SubmittedAt = new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero),
            NameMatches = true,
            CitizenIdMatches = true,
            DateOfBirthMatches = true,
            ReviewerFullName = "Luật sư Dũng",
            ReviewerLawyerCardNumber = "LS-12345",
            ReviewedAt = new DateTimeOffset(2026, 11, 9, 4, 0, 0, TimeSpan.Zero),
        };

        byte[] image = new QuestPdfDeclinePaperRenderer().Render(data);

        Assert.True(image.Length > 500);
        Assert.Equal("PNG", Encoding.ASCII.GetString(image, 1, 3));
    }

    [Fact]
    public void Render_Acceptance_ProducesPngBytes()
    {
        DeclinePaperData data = new DeclinePaperData
        {
            TurnCode = "AB12CD34",
            ContainerName = "Gói gia đình",
            OwnerFullName = "Nguyễn Văn A",
            DeclinerFullName = "Trần Minh Anh",
            CitizenIdLast4 = "1234",
            SubmittedAt = new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero),
            NameMatches = true,
            CitizenIdMatches = true,
            DateOfBirthMatches = true,
            ReviewerFullName = "Luật sư Dũng",
            ReviewerLawyerCardNumber = "LS-12345",
            ReviewedAt = new DateTimeOffset(2026, 11, 9, 4, 0, 0, TimeSpan.Zero),
            IsAcceptance = true,
        };

        byte[] image = new QuestPdfDeclinePaperRenderer().Render(data);

        Assert.True(image.Length > 500);
        Assert.Equal("PNG", Encoding.ASCII.GetString(image, 1, 3));
    }

    [Fact]
    public void DeathSentence_PrintsDatesAndPlaceInTheAgreedWording()
    {
        // Chuẩn bị
        DeclinePaperData data = new DeclinePaperData();
        data.DateOfDeath = new DateOnly(2026, 11, 5);
        data.DeathCertificateIssuedOn = new DateOnly(2026, 11, 7);
        data.DeathRegistrationPlace = "UBND phường Bến Thành, Thành phố Hồ Chí Minh";
        data.CertificateNumber = "0456/2026";

        // Hành động
        string sentence = QuestPdfDeclinePaperRenderer.DeathSentence(data);

        // Kiểm tra: đúng câu đã chốt, không còn phần "số ..." của số giấy chứng tử
        Assert.Equal(
            "chết ngày 05/11/2026 theo giấy chứng tử do UBND phường Bến Thành, Thành phố Hồ Chí Minh "
            + "đăng ký khai tử ngày 07/11/2026.",
            sentence);
        Assert.DoesNotContain("số", sentence);
        Assert.DoesNotContain("0456", sentence);
    }

    [Fact]
    public void DeathSentence_PrintsDottedLinesWhenDataIsMissing()
    {
        // Chuẩn bị: hồ sơ chưa có ngày mất, ngày đăng ký và nơi đăng ký
        DeclinePaperData data = new DeclinePaperData();

        // Hành động
        string sentence = QuestPdfDeclinePaperRenderer.DeathSentence(data);

        // Kiểm tra
        Assert.Equal(
            "chết ngày …………… theo giấy chứng tử do UBND ………………… đăng ký khai tử ngày ………………………….",
            sentence);
    }

    [Fact]
    public void Render_WithDeathRegistrationPlace_ProducesPngBytes()
    {
        // Chuẩn bị
        DeclinePaperData data = new DeclinePaperData();
        data.TurnCode = "AB12CD34";
        data.ContainerName = "Gói gia đình";
        data.OwnerFullName = "Nguyễn Văn A";
        data.DeclinerFullName = "Trần Minh Anh";
        data.CitizenIdLast4 = "1234";
        data.SubmittedAt = new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero);
        data.DateOfDeath = new DateOnly(2026, 11, 5);
        data.DeathCertificateIssuedOn = new DateOnly(2026, 11, 7);
        data.DeathRegistrationPlace = "UBND phường Bến Thành, Thành phố Hồ Chí Minh";

        // Hành động: cả văn bản từ chối và biên bản đồng ý đều dựng được
        data.IsAcceptance = false;
        byte[] decline = new QuestPdfDeclinePaperRenderer().Render(data);
        data.IsAcceptance = true;
        byte[] acceptance = new QuestPdfDeclinePaperRenderer().Render(data);

        // Kiểm tra
        Assert.Equal("PNG", Encoding.ASCII.GetString(decline, 1, 3));
        Assert.Equal("PNG", Encoding.ASCII.GetString(acceptance, 1, 3));
    }
}
