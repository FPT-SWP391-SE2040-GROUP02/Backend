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
