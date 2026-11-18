using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Biên bản bàn giao của người nhận: tạo biên bản cho một lượt, xem và liệt kê theo từng vai (người nhận, Người thực thi, Người xét duyệt).</summary>
public interface IHandoverDocumentService
{
    /// <summary>
    /// Người nhận chọn Chấp nhận hoặc Từ chối để bắt đầu biên bản. Chỉ khi lượt ở bước xác minh danh tính.
    /// Phần 1 (thông tin về Chủ kho) được chép luôn từ hồ sơ chứng tử đã duyệt, biên bản vào thẳng bước người nhận.
    /// Gọi lại cùng lựa chọn thì trả biên bản cũ; người nhận chưa ký thì được đổi lựa chọn.
    /// Lỗi: 404 lượt không phải của mình; 409 WRONG_STATE, DOCUMENT_STARTED.
    /// </summary>
    Task<HandoverDocumentResponse> CreateAsync(Guid turnId, CreateHandoverDocumentRequest request, CancellationToken ct);

    /// <summary>Biên bản của lượt mình đã nhận; null khi chưa bắt đầu.</summary>
    Task<HandoverDocumentResponse?> GetByTurnAsync(Guid turnId, CancellationToken ct);

    /// <summary>Mọi biên bản của người nhận, mới nhất trước (mục Biên Bản). Không giới hạn thời gian.</summary>
    Task<List<HandoverDocumentResponse>> ListMineAsync(CancellationToken ct);

    /// <summary>Biên bản của các kho mà mình là Người thực thi đang hiệu lực, mới nhất trước (chỉ để xem lại).</summary>
    Task<List<HandoverDocumentResponse>> ListForExecutorAsync(CancellationToken ct);

    /// <summary>Một biên bản của kho mình làm Người thực thi; kho khác hoặc id lạ cùng một lỗi 404.</summary>
    Task<HandoverDocumentResponse> GetForExecutorAsync(Guid documentId, CancellationToken ct);

    /// <summary>Biên bản của các kho mà Người xét duyệt này đã duyệt hồ sơ chứng tử, mới nhất trước.</summary>
    Task<List<HandoverDocumentResponse>> ListForVerifierAsync(CancellationToken ct);
}
