using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Handover;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Câu báo "người nhận từ chối thì tài sản bị xóa hoàn toàn khỏi hệ thống" trong biên bản đồng ý: có mức gốc, Admin sửa được
/// bằng tham số Handover.DeclineNotice, giới hạn độ dài, và được in vào biên bản.
/// </summary>
[Collection("RuntimeSettings")]
public sealed class HandoverDeclineNoticeTests
{
    [Fact]
    public void DeclineNotice_WithoutSetting_UsesDefaultThatSaysDeletedCompletely()
    {
        RuntimeSettings.Replace(new Dictionary<string, string>());

        string notice = HandoverDocumentRules.DeclineNotice;

        Assert.Equal(HandoverDocumentRules.DefaultDeclineNotice, notice);
        Assert.Contains("xóa hoàn toàn khỏi hệ thống", notice);
    }

    [Fact]
    public void DeclineNotice_AdminSetting_OverridesDefault_AndBlankFallsBack()
    {
        try
        {
            RuntimeSettings.Replace(new Dictionary<string, string>
            {
                [HandoverDocumentRules.DeclineNoticeSettingKey] = "  Tài sản của người từ chối sẽ bị hủy ngay.  ",
            });
            Assert.Equal("Tài sản của người từ chối sẽ bị hủy ngay.", HandoverDocumentRules.DeclineNotice);

            RuntimeSettings.Replace(new Dictionary<string, string>
            {
                [HandoverDocumentRules.DeclineNoticeSettingKey] = "   ",
            });
            Assert.Equal(HandoverDocumentRules.DefaultDeclineNotice, HandoverDocumentRules.DeclineNotice);
        }
        finally
        {
            RuntimeSettings.Replace(new Dictionary<string, string>());
        }
    }

    [Fact]
    public void Setting_TooLong_IsRejected_AndNormalLengthIsAccepted()
    {
        string tooLong = new string('a', 401);
        string fine = new string('a', 400);

        Assert.NotNull(SystemSettingRules.FindValueError(HandoverDocumentRules.DeclineNoticeSettingKey, tooLong));
        Assert.Null(SystemSettingRules.FindValueError(HandoverDocumentRules.DeclineNoticeSettingKey, fine));
    }

    [Fact]
    public void PaperData_DefaultsToCurrentNotice_AndAcceptancePaperStillRenders()
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

        byte[] image = new QuestPdfDeclinePaperRenderer().Render(data);

        Assert.Equal(HandoverDocumentRules.DefaultDeclineNotice, data.DeclineNotice);
        Assert.True(image.Length > 500);
    }
}
