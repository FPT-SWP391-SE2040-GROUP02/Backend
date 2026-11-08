using System.Text;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Handover;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>PDF văn bản từ chối: dựng được tệp PDF hợp lệ từ dữ liệu hệ thống đã giữ.</summary>
public sealed class QuestPdfDeclinePaperRendererTests
{
    [Fact]
    public void Render_ProducesPdfBytes()
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

        byte[] pdf = new QuestPdfDeclinePaperRenderer().Render(data);

        Assert.True(pdf.Length > 500);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }
}
