using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Mã tải một lần qua email: tắt thì không cần, bật thì cần đúng mã, dùng một lần, hết hạn, nhập sai nhiều lần bị hủy.</summary>
[Collection("RuntimeSettings")]
public sealed class DownloadCodeServiceTests : IDisposable
{
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly CapturingSender sender = new CapturingSender();
    private readonly TestUser current = new TestUser();
    private readonly MutableTime time = new MutableTime();
    private readonly Guid grantId = Guid.NewGuid();

    public DownloadCodeServiceTests()
    {
        User user = new User();
        user.Email = "nguoi.nhan@test.local";
        user.FullName = "Người nhận";
        user.Role = UserRole.User;
        user.Status = UserStatus.Active;
        users.Users.Add(user);
        current.UserId = user.Id;
        SetRequired(true);
    }

    public void Dispose()
    {
        SetRequired(false);
    }

    private static void SetRequired(bool required)
    {
        RuntimeSettings.Replace(new Dictionary<string, string>
        {
            ["Handover.DownloadCodeRequired"] = required ? "true" : "false",
        });
    }

    private DownloadCodeService Create()
    {
        return new DownloadCodeService(current, users, sender, time);
    }

    private static string ErrorOf(Action action)
    {
        AppException exception = Assert.Throws<AppException>(action);
        Assert.Equal(AppErrorType.Forbidden, exception.Type);
        return exception.Code.ToString();
    }

    [Fact]
    public void Disabled_AllowsDownloadWithoutCode()
    {
        SetRequired(false);
        DownloadCodeService service = Create();

        Assert.False(service.Required);
        service.Consume(grantId, null);
    }

    [Fact]
    public void Enabled_WithoutCode_IsRefused()
    {
        DownloadCodeService service = Create();

        Assert.True(service.Required);
        Assert.Equal("DownloadCodeRequired", ErrorOf(() => service.Consume(grantId, null)));
        Assert.Equal("DownloadCodeRequired", ErrorOf(() => service.Consume(grantId, "  ")));
    }

    [Fact]
    public async Task RequestedCode_IsEmailedSixDigits_AndWorksOnlyOnce()
    {
        DownloadCodeService service = Create();

        await service.RequestAsync(grantId, CancellationToken.None);

        Assert.Equal("nguoi.nhan@test.local", sender.ToEmail);
        Assert.Matches("^[0-9]{6}$", sender.Code);
        service.Consume(grantId, sender.Code);
        // Dùng rồi thì mã hết tác dụng.
        Assert.Equal("DownloadCodeInvalid", ErrorOf(() => service.Consume(grantId, sender.Code)));
    }

    [Fact]
    public async Task WrongCode_IsRefused_AndFiveMistakesCancelTheCode()
    {
        DownloadCodeService service = Create();
        await service.RequestAsync(grantId, CancellationToken.None);
        string wrong = sender.Code == "000000" ? "111111" : "000000";

        for (int attempt = 0; attempt < DownloadCodeService.MaxAttempts; attempt++)
        {
            Assert.Equal("DownloadCodeInvalid", ErrorOf(() => service.Consume(grantId, wrong)));
        }

        // Quá số lần: ngay cả mã đúng cũng không còn dùng được, phải xin mã mới.
        Assert.Equal("DownloadCodeInvalid", ErrorOf(() => service.Consume(grantId, sender.Code)));
    }

    [Fact]
    public async Task Code_ExpiresAfterTenMinutes()
    {
        DownloadCodeService service = Create();
        await service.RequestAsync(grantId, CancellationToken.None);

        time.Now = time.Now.Add(DownloadCodeService.Lifetime).AddSeconds(1);

        Assert.Equal("DownloadCodeInvalid", ErrorOf(() => service.Consume(grantId, sender.Code)));
    }

    [Fact]
    public async Task Code_IsBoundToTheGrant_AndNewRequestReplacesTheOldOne()
    {
        DownloadCodeService service = Create();
        await service.RequestAsync(grantId, CancellationToken.None);
        string first = sender.Code;

        Assert.Equal("DownloadCodeInvalid", ErrorOf(() => service.Consume(Guid.NewGuid(), first)));

        await service.RequestAsync(grantId, CancellationToken.None);
        string second = sender.Code;
        if (second != first)
        {
            Assert.Equal("DownloadCodeInvalid", ErrorOf(() => service.Consume(grantId, first)));
        }
        service.Consume(grantId, second);
    }

    [Fact]
    public async Task SendFailure_LeavesNoUsableCode()
    {
        DownloadCodeService service = Create();
        sender.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestAsync(grantId, CancellationToken.None));

        Assert.Equal("DownloadCodeInvalid", ErrorOf(() => service.Consume(grantId, "123456")));
    }

    private sealed class CapturingSender : IDownloadCodeSender
    {
        public string ToEmail { get; private set; } = string.Empty;

        public string Code { get; private set; } = string.Empty;

        public bool Fail { get; set; }

        public Task SendAsync(string toEmail, string fullName, string code, DateTimeOffset expiresAt, CancellationToken ct)
        {
            if (Fail)
            {
                throw new InvalidOperationException("gửi lỗi");
            }
            ToEmail = toEmail;
            Code = code;
            return Task.CompletedTask;
        }
    }

    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId { get; set; }

        public bool IsAuthenticated => UserId != null;
    }

    private sealed class MutableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}
