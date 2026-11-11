using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>Báo cho đúng người khi biên bản chuyển bước. Gửi sau khi đã lưu; gửi lỗi không làm hỏng việc đã lưu.</summary>
public interface IHandoverDocumentNotifier
{
    /// <summary>Người nhận đã ký: báo Người xét duyệt đã duyệt hồ sơ chứng tử của kho.</summary>
    Task VerifierTurnAsync(HandoverDocument document, CancellationToken ct);

    /// <summary>Người xét duyệt trả về: báo người nhận kèm lý do.</summary>
    Task RecipientReturnedAsync(HandoverDocument document, string reason, CancellationToken ct);

    /// <summary>Biên bản hoàn tất: báo người nhận và Người thực thi.</summary>
    Task CompletedAsync(HandoverDocument document, CancellationToken ct);
}

public sealed class HandoverDocumentNotifier : IHandoverDocumentNotifier
{
    private readonly IHandoverDocumentNoticeSender sender;
    private readonly IUserRepository users;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverSubmissionRepository submissions;

    public HandoverDocumentNotifier(
        IHandoverDocumentNoticeSender sender,
        IUserRepository users,
        IExecutorReader executorReader,
        IHandoverSubmissionRepository submissions)
    {
        this.sender = sender;
        this.users = users;
        this.executorReader = executorReader;
        this.submissions = submissions;
    }

    public async Task VerifierTurnAsync(HandoverDocument document, CancellationToken ct)
    {
        Guid? verifierId = await submissions.FindApprovingVerifierUserIdAsync(document.VaultId, ct);
        await SendToUserAsync(document, verifierId, HandoverDocumentNoticeKind.VerifierTurn, null, ct);
    }

    public async Task RecipientReturnedAsync(HandoverDocument document, string reason, CancellationToken ct)
    {
        await SendToUserAsync(document, document.Turn.ClaimantUserId, HandoverDocumentNoticeKind.RecipientReturned, reason, ct);
    }

    public async Task CompletedAsync(HandoverDocument document, CancellationToken ct)
    {
        await SendToUserAsync(document, document.Turn.ClaimantUserId, HandoverDocumentNoticeKind.Completed, null, ct);
        await SendToExecutorsAsync(document, HandoverDocumentNoticeKind.Completed, ct);
    }

    private async Task SendToExecutorsAsync(HandoverDocument document, HandoverDocumentNoticeKind kind, CancellationToken ct)
    {
        List<Executor> executors;
        try
        {
            executors = await executorReader.GetActiveAsync(document.VaultId, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return;
        }
        foreach (Executor executor in executors)
        {
            await SendToUserAsync(document, executor.UserId, kind, null, ct);
        }
    }

    private async Task SendToUserAsync(
        HandoverDocument document, Guid? userId, HandoverDocumentNoticeKind kind, string? reason, CancellationToken ct)
    {
        if (userId == null)
        {
            return;
        }
        try
        {
            User? user = await users.FindByIdAsync(userId.Value, ct);
            if (user == null)
            {
                return;
            }
            HandoverDocumentNotice notice = new HandoverDocumentNotice();
            notice.Kind = kind;
            notice.ToEmail = user.Email;
            notice.FullName = user.FullName;
            notice.DocumentKind = document.Kind == HandoverSubmissionKind.Accept ? "ACCEPT" : "DECLINE";
            notice.DocumentCode = HandoverDocumentRules.Code(document.Id);
            notice.RecipientName = document.RecipientFullName;
            notice.Reason = reason;
            await sender.SendAsync(notice, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Thư báo là phụ: lỗi gửi không làm hỏng bước đã lưu. Người nhận thư vẫn thấy việc trong mục Biên Bản.
        }
    }
}
