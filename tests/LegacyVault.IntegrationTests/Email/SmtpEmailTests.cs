using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;

namespace LegacyVault.IntegrationTests.Email;

/// <summary>Dịch vụ email Gmail qua MailKit: nội dung thư, gửi qua SMTP giả, cấu hình và chọn bản cài đặt.</summary>
public sealed class SmtpEmailTests
{
    // 08:00 UTC = 15:00 giờ Việt Nam (UTC+7).
    private static readonly DateTimeOffset Due = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GraceEnds = new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);
    private const string CheckInUrl = "http://localhost:5173/checkin";

    private static SmtpSettings SettingsFor(FakeSmtpServer server)
    {
        SmtpSettings settings = new SmtpSettings();
        settings.Host = "127.0.0.1";
        settings.Port = server.Port;
        settings.SecureSocket = "None";
        settings.FromAddress = "no-reply@legacyvault.test";
        settings.FromName = "LegacyVault";
        return settings;
    }

    private static MimeMessage ParseOnlyMessage(FakeSmtpServer server)
    {
        byte[] raw = Assert.Single(server.ReceivedMessages);
        using (MemoryStream stream = new MemoryStream(raw))
        {
            return MimeMessage.Load(stream);
        }
    }

    // ---------- Nội dung thư nhắc ----------

    [Fact]
    public void Build_EachReminderKindHasItsOwnSubject()
    {
        List<string> subjects = new List<string>();
        foreach (CheckInReminderKind kind in Enum.GetValues<CheckInReminderKind>())
        {
            EmailContent content = CheckInReminderEmail.Build(kind, "Nguyễn Văn An", Due, GraceEnds, CheckInUrl);
            Assert.False(string.IsNullOrWhiteSpace(content.Subject));
            subjects.Add(content.Subject);
        }

        Assert.Equal(4, subjects.Distinct().Count());
    }

    [Fact]
    public void Build_TextMentionsNameVietnamTimeAndLink()
    {
        EmailContent content = CheckInReminderEmail.Build(
            CheckInReminderKind.DueInSevenDays, "Nguyễn Văn An", Due, GraceEnds, CheckInUrl);

        Assert.Contains("Nguyễn Văn An", content.TextBody);
        Assert.Contains("15:00", content.TextBody);
        Assert.Contains("31/10/2026", content.TextBody);
        Assert.Contains(CheckInUrl, content.TextBody);
        Assert.Contains(CheckInUrl, content.HtmlBody);
    }

    [Fact]
    public void Build_GraceStartedMentionsGraceEnd()
    {
        EmailContent content = CheckInReminderEmail.Build(
            CheckInReminderKind.GraceStarted, "An", Due, GraceEnds, CheckInUrl);

        Assert.Contains("07/11/2026", content.TextBody);
    }

    [Fact]
    public void Build_HtmlEncodesUserSuppliedName()
    {
        EmailContent content = CheckInReminderEmail.Build(
            CheckInReminderKind.DueInOneDay, "<b>Hack</b> & Co", Due, GraceEnds, CheckInUrl);

        Assert.Contains("&lt;b&gt;Hack&lt;/b&gt;", content.HtmlBody);
        Assert.DoesNotContain("<b>Hack</b>", content.HtmlBody);
    }

    // ---------- Gửi qua SMTP (máy chủ giả) ----------

    [Fact]
    public async Task MailSender_DeliversMessageWithVietnameseSubjectAndBothBodies()
    {
        using FakeSmtpServer server = new FakeSmtpServer();
        SmtpMailSender sender = new SmtpMailSender(Options.Create(SettingsFor(server)));

        await sender.SendAsync("an@test.local", "Nguyễn Văn An", "Tiêu đề có dấu",
            "Nội dung chữ thuần", "<p>Nội dung HTML</p>", CancellationToken.None);

        MimeMessage message = ParseOnlyMessage(server);
        Assert.Equal("Tiêu đề có dấu", message.Subject);
        Assert.Equal("no-reply@legacyvault.test", message.From.Mailboxes.Single().Address);
        MailboxAddress to = message.To.Mailboxes.Single();
        Assert.Equal("an@test.local", to.Address);
        Assert.Equal("Nguyễn Văn An", to.Name);
        Assert.Contains("Nội dung chữ thuần", message.TextBody);
        Assert.Contains("Nội dung HTML", message.HtmlBody);
    }

    [Fact]
    public async Task CheckInReminderSender_SendsReminderPointingToClientCheckInPage()
    {
        using FakeSmtpServer server = new FakeSmtpServer();
        SmtpMailSender mailSender = new SmtpMailSender(Options.Create(SettingsFor(server)));
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:ClientUrl"] = "http://localhost:5173/" })
            .Build();
        SmtpCheckInReminderSender sender = new SmtpCheckInReminderSender(mailSender, configuration);

        await sender.SendReminderAsync("an@test.local", "Nguyễn Văn An",
            CheckInReminderKind.GraceStarted, Due, GraceEnds, CancellationToken.None);

        MimeMessage message = ParseOnlyMessage(server);
        Assert.Equal("an@test.local", message.To.Mailboxes.Single().Address);
        Assert.Contains("http://localhost:5173/checkin", message.TextBody);
        Assert.DoesNotContain("//checkin", message.TextBody);
    }

    [Fact]
    public async Task VerificationSender_SendsLinkToRecipient()
    {
        using FakeSmtpServer server = new FakeSmtpServer();
        SmtpMailSender mailSender = new SmtpMailSender(Options.Create(SettingsFor(server)));
        SmtpEmailSender sender = new SmtpEmailSender(mailSender);
        string url = "http://localhost:5173/verify-email?token=abc123";

        await sender.SendEmailVerificationAsync("an@test.local", "Nguyễn Văn An", url, CancellationToken.None);

        MimeMessage message = ParseOnlyMessage(server);
        Assert.Contains(url, message.TextBody);
        Assert.Contains(url, message.HtmlBody);
    }

    [Fact]
    public async Task MailSender_ServerRejectsRecipient_ThrowsSoTheJobCanRetry()
    {
        using FakeSmtpServer server = new FakeSmtpServer();
        server.RejectRecipientWith = "550 no such user";
        SmtpMailSender sender = new SmtpMailSender(Options.Create(SettingsFor(server)));

        await Assert.ThrowsAnyAsync<Exception>(() => sender.SendAsync(
            "ghost@test.local", "Ghost", "Chủ đề", "nội dung", "<p>nội dung</p>", CancellationToken.None));

        Assert.Empty(server.ReceivedMessages);
    }

    // ---------- Cấu hình ----------

    [Theory]
    [InlineData("smtp.gmail.com", "user@gmail.com", "app-password", true)]
    [InlineData("smtp.gmail.com", "", "app-password", false)]
    [InlineData("smtp.gmail.com", "user@gmail.com", "", false)]
    [InlineData("", "user@gmail.com", "app-password", false)]
    public void Settings_IsConfiguredNeedsHostUserAndPassword(string host, string user, string password, bool expected)
    {
        SmtpSettings settings = new SmtpSettings();
        settings.Host = host;
        settings.UserName = user;
        settings.Password = password;

        Assert.Equal(expected, settings.IsConfigured);
    }

    [Fact]
    public void Settings_DefaultsMatchGmailStartTls()
    {
        SmtpSettings settings = new SmtpSettings();

        Assert.Equal("smtp.gmail.com", settings.Host);
        Assert.Equal(587, settings.Port);
        Assert.Equal("StartTls", settings.SecureSocket);
    }

    [Fact]
    public void Settings_FromAddressFallsBackToUserName()
    {
        SmtpSettings settings = new SmtpSettings();
        settings.UserName = "user@gmail.com";

        Assert.Equal("user@gmail.com", settings.ResolveFromAddress());
        settings.FromAddress = "no-reply@legacyvault.test";
        Assert.Equal("no-reply@legacyvault.test", settings.ResolveFromAddress());
    }

    [Theory]
    [InlineData(0, "StartTls", "user@gmail.com")]
    [InlineData(70000, "StartTls", "user@gmail.com")]
    [InlineData(587, "Nope", "user@gmail.com")]
    [InlineData(587, "StartTls", "not-an-email")]
    public void Settings_FindErrorReportsInvalidValuesWhenConfigured(int port, string secureSocket, string fromAddress)
    {
        SmtpSettings settings = new SmtpSettings();
        settings.UserName = "user@gmail.com";
        settings.Password = "app-password";
        settings.Port = port;
        settings.SecureSocket = secureSocket;
        settings.FromAddress = fromAddress;

        Assert.NotNull(settings.FindError());
    }

    [Fact]
    public void Settings_FindErrorIsNullWhenValidOrNotConfigured()
    {
        SmtpSettings valid = new SmtpSettings();
        valid.UserName = "user@gmail.com";
        valid.Password = "app-password";
        SmtpSettings notConfigured = new SmtpSettings();
        notConfigured.Port = 0;

        Assert.Null(valid.FindError());
        Assert.Null(notConfigured.FindError());
    }

    // ---------- Chọn bản cài đặt theo cấu hình ----------

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEmailServices(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddEmailServices_WithoutCredentials_UsesLogOnlySenders()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>());

        Assert.IsType<LogCheckInReminderSender>(provider.GetRequiredService<ICheckInReminderSender>());
        Assert.IsType<DevEmailSender>(provider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void AddEmailServices_WithCredentials_UsesSmtpSenders()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Email:Smtp:UserName"] = "user@gmail.com",
            ["Email:Smtp:Password"] = "app-password",
        });

        Assert.IsType<SmtpCheckInReminderSender>(provider.GetRequiredService<ICheckInReminderSender>());
        Assert.IsType<SmtpEmailSender>(provider.GetRequiredService<IEmailSender>());
    }
}
