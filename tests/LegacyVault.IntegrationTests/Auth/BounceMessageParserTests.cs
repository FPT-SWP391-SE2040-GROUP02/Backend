using LegacyVault.Infrastructure.Notifications;
using MimeKit;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

public sealed class BounceMessageParserTests
{
    private static MimeMessage Dsn(string action, string status)
    {
        string raw = "From: Mail Delivery Subsystem <mailer-daemon@googlemail.com>\r\nTo: a@b.com\r\nSubject: Delivery Status Notification (Failure)\r\n"
            + "MIME-Version: 1.0\r\nContent-Type: multipart/report; report-type=delivery-status; boundary=\"XX\"\r\n\r\n"
            + "--XX\r\nContent-Type: text/plain\r\n\r\nKhong tim thay dia chi\r\n"
            + "--XX\r\nContent-Type: message/delivery-status\r\n\r\n"
            + "Reporting-MTA: dns; googlemail.com\r\n\r\n"
            + $"Final-Recipient: rfc822; Ghost@Legacyvault.vn\r\nAction: {action}\r\nStatus: {status}\r\n\r\n"
            + "--XX--\r\n";
        using MemoryStream stream = new(System.Text.Encoding.ASCII.GetBytes(raw));
        return MimeMessage.Load(stream);
    }

    [Fact]
    public void PermanentFailure_ReturnsLowercasedRecipientAndCode()
    {
        var result = BounceMessageParser.Parse(Dsn("failed", "5.4.4"));
        Assert.Single(result);
        Assert.Equal("ghost@legacyvault.vn", result[0].Email);
        Assert.Equal("5.4.4", result[0].Code);
    }

    [Fact]
    public void Delayed_IsIgnored()
    {
        Assert.Empty(BounceMessageParser.Parse(Dsn("delayed", "4.4.1")));
    }

    [Fact]
    public void WeirdStatus_FallsBackToDefaultCode()
    {
        Assert.Equal("5.0.0", BounceMessageParser.Parse(Dsn("failed", "<script>"))[0].Code);
    }
}
