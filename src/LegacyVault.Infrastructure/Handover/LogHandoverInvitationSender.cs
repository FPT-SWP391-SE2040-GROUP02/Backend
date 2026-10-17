using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Bản gửi lời mời cho môi trường chưa cấu hình email: chỉ ghi log, không gửi thư thật.
/// Ở Development, log in token để bạn tự dựng link thử; môi trường khác tuyệt đối không ghi token hay email (OPS-04).
/// </summary>
public sealed class LogHandoverInvitationSender : IHandoverInvitationSender
{
    private readonly ILogger<LogHandoverInvitationSender> logger;
    private readonly IHostEnvironment environment;

    public LogHandoverInvitationSender(ILogger<LogHandoverInvitationSender> logger, IHostEnvironment environment)
    {
        this.logger = logger;
        this.environment = environment;
    }

    public Task SendAsync(HandoverInvitation invitation, CancellationToken ct)
    {
        if (environment.IsDevelopment())
        {
            logger.LogInformation("[LỜI MỜI NHẬN TÀI SẢN] Lượt {TurnId}; mở /receive?token={Token}",
                invitation.TurnId, invitation.Token);
        }
        else
        {
            logger.LogWarning("[LỜI MỜI NHẬN TÀI SẢN] Lượt {TurnId}; chưa cấu hình email nên không gửi thư.", invitation.TurnId);
            // Không giả vờ đã gửi: HandoverStarter giữ InvitationSentAt = null.
            throw new InvalidOperationException("Chưa cấu hình Email:Smtp nên không gửi được lời mời.");
        }
        return Task.CompletedTask;
    }
}
