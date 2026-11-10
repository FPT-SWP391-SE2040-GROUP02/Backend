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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Render_LongestAddressesWithSignatures_StillFitsOnOnePage(bool acceptance)
    {
        // Trường hợp xấu nhất: hai nơi thường trú dài tối đa và đủ khối chữ ký. Chỉ trang đầu thành ảnh,
        // nên nội dung tràn trang là mất khối chữ ký; renderer phải thu nhỏ chữ cho vừa một trang.
        string longAddress = string.Concat(Enumerable.Repeat("Số 123 ngõ 45 ", 40))
            .Substring(0, HandoverDocumentRules.ResidenceAddressMaxLength);
        DeclinePaperData data = new DeclinePaperData
        {
            IsAcceptance = acceptance,
            TurnCode = "AB12CD34",
            ContainerName = "Gói gia đình có tên khá dài để chiếm thêm một dòng trong văn bản",
            OwnerFullName = "Nguyễn Văn A",
            OwnerResidenceAddress = longAddress,
            DeclinerFullName = "Trần Minh Anh",
            RecipientResidenceAddress = longAddress,
            RecipientBirthYear = 1990,
            CitizenIdLast4 = "1234",
            CertificateNumber = "0456/2026",
            ItemCount = 12,
            DateOfDeath = new DateOnly(2026, 11, 5),
            DeathCertificateIssuedOn = new DateOnly(2026, 11, 7),
            SubmittedAt = new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero),
            HasSignatures = true,
            RecipientSignedAt = new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero),
            ReviewerFullName = "Luật sư Dũng",
            ReviewedAt = new DateTimeOffset(2026, 11, 9, 4, 0, 0, TimeSpan.Zero),
        };

        Assert.Equal(1, QuestPdfDeclinePaperRenderer.PageCountAtSmallestFont(data));
        byte[] image = new QuestPdfDeclinePaperRenderer().Render(data);
        Assert.Equal("PNG", Encoding.ASCII.GetString(image, 1, 3));
    }
}
