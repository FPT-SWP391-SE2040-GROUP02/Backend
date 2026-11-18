using LegacyVault.Application.Mappings;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>Báo cho đúng người khi biên bản chuyển bước. Gửi sau khi đã lưu; gửi lỗi không làm hỏng việc đã lưu.</summary>
public interface IHandoverDocumentNotifier
{
    /// <summary>Người nhận đã ký: báo Người xét duyệt đã duyệt hồ sơ chứng tử của kho.</summary>
    Task VerifierTurnAsync(HandoverDocument document, CancellationToken ct);

    /// <summary>Biên bản hoàn tất: báo người nhận và Người thực thi.</summary>
    Task CompletedAsync(HandoverDocument document, CancellationToken ct);
}

/// <summary>Gửi thư báo về biên bản: báo Người xét duyệt khi người nhận đã ký, báo người nhận và Người thực thi khi biên bản hoàn tất.</summary>
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

    /// <summary>Người nhận đã ký: báo cho Người xét duyệt đã duyệt hồ sơ chứng tử của kho.</summary>
    public async Task VerifierTurnAsync(HandoverDocument document, CancellationToken ct)
    {
        Guid? verifierId = await submissions.FindApprovingVerifierUserIdAsync(document.VaultId, ct);
        await SendToUserAsync(document, verifierId, HandoverDocumentNoticeKind.VerifierTurn, ct);
    }

    /// <summary>Biên bản hoàn tất: báo cho người nhận và cho Người thực thi của kho.</summary>
    public async Task CompletedAsync(HandoverDocument document, CancellationToken ct)
    {
        await SendToUserAsync(document, document.Turn.ClaimantUserId, HandoverDocumentNoticeKind.Completed, ct);
        await SendToExecutorsAsync(document, HandoverDocumentNoticeKind.Completed, ct);
    }

    /// <summary>Báo cho mọi Người thực thi đang hiệu lực của kho; không đọc được danh sách thì bỏ qua.</summary>
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
            await SendToUserAsync(document, executor.UserId, kind, ct);
        }
    }

    /// <summary>Gửi thư báo biên bản cho một người. Không có người nhận hoặc gửi lỗi thì bỏ qua, không ném lỗi.</summary>
    private async Task SendToUserAsync(
        HandoverDocument document, Guid? userId, HandoverDocumentNoticeKind kind, CancellationToken ct)
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
            HandoverDocumentNotice notice = new HandoverDocumentNotice
            {
                Kind = kind,
                ToEmail = user.Email,
                FullName = user.FullName,
                DocumentKind = document.Kind.ToApiString(),
                DocumentCode = HandoverDocumentRules.Code(document.Id),
                RecipientName = document.RecipientFullName
            };
            await sender.SendAsync(notice, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Thư báo là phụ: lỗi gửi không làm hỏng bước đã lưu. Người nhận thư vẫn thấy việc trong mục Biên Bản.
        }
    }
}
