using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// Biên bản bàn giao: căn cứ duy nhất để giao tài sản. Hệ thống chỉ quản lý tài sản, không làm pháp lý.
/// Người thực thi mở bàn giao; luật sư lập, chốt, mở lại và ký; người nhận ký; đủ chữ ký thì mở quyền tải.
/// </summary>
public interface IHandoverRecordService
{
    // Người thực thi

    /// <summary>Các kho tôi đang là Người thực thi, kèm tình trạng biên bản bàn giao.</summary>
    Task<List<ExecutorVaultResponse>> ListMyVaultsAsync(CancellationToken ct);

    /// <summary>Biên bản của kho; null khi chưa mở bàn giao. Lỗi: 404 VAULT_NOT_FOUND khi không phải Người thực thi.</summary>
    Task<HandoverRecordResponse?> GetForExecutorAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Người thực thi ký biên bản ở bước 3.</summary>
    Task<HandoverRecordResponse> SignAsExecutorAsync(Guid vaultId, CancellationToken ct);

    // Luật sư

    Task<List<HandoverRecordSummaryResponse>> ListForVerifierAsync(CancellationToken ct);
    Task<HandoverRecordResponse> GetForVerifierAsync(Guid recordId, CancellationToken ct);

    /// <summary>Lấy biên bản phân chia của Chủ kho (các gói sẵn sàng) làm bản đầu, thay toàn bộ dòng đang có.</summary>
    Task<HandoverRecordResponse> UseOwnerSplitAsync(Guid recordId, CancellationToken ct);

    /// <summary>Thay toàn bộ dòng "ai nhận tài sản nào". Chỉ khi biên bản đang lập (bước 1).</summary>
    Task<HandoverRecordResponse> SetLinesAsync(Guid recordId, SetHandoverRecordLinesRequest request, CancellationToken ct);

    /// <summary>Chốt biên bản (xong bước 1): tạo lượt cho từng người nhận và gửi thư mời.</summary>
    Task<HandoverRecordResponse> FinalizeAsync(Guid recordId, CancellationToken ct);

    /// <summary>Mở lại biên bản đã chốt để sửa: mọi chữ ký cũ mất hiệu lực.</summary>
    Task<HandoverRecordResponse> ReopenAsync(Guid recordId, CancellationToken ct);

    /// <summary>Luật sư ký xác minh ở bước 3.</summary>
    Task<HandoverRecordResponse> SignAsVerifierAsync(Guid recordId, CancellationToken ct);

    /// <summary>Xét giấy tờ của một yêu cầu nhận: chấp nhận, yêu cầu bổ sung (kèm lý do) hoặc từ chối (kèm lý do).</summary>
    Task<HandoverRecordResponse> ReviewClaimAsync(
        Guid recordId, Guid claimId, ReviewHandoverClaimRequest request, CancellationToken ct);

    /// <summary>Luật sư thêm người nhận mới vào kho khi lập biên bản.</summary>
    Task<HandoverRecordResponse> AddRecipientAsync(Guid recordId, AddHandoverRecipientRequest request, CancellationToken ct);

    /// <summary>Luật sư đính kèm tệp căn cứ (di chúc, văn bản...) vào biên bản.</summary>
    Task<HandoverRecordResponse> AddEvidenceAsync(Guid recordId, List<UploadedFile> files, CancellationToken ct);

    Task<HandoverRecordResponse> RemoveEvidenceAsync(Guid recordId, Guid fileId, CancellationToken ct);

    /// <summary>Tải một tệp đính kèm biên bản (giấy tờ người nhận hoặc tệp căn cứ); chỉ luật sư của biên bản.</summary>
    Task<PreparedDownload> PrepareVerifierFileAsync(Guid recordId, Guid fileId, CancellationToken ct);

    // Người nhận gửi yêu cầu nhận tài sản

    /// <summary>Thông tin link riêng (không cần đăng nhập): tên Chủ kho, tên người nhận, email đã che.</summary>
    Task<HandoverRequestPreviewResponse> PreviewRequestAsync(string token, CancellationToken ct);

    /// <summary>
    /// Gửi yêu cầu nhận tài sản kèm giấy tờ. Yêu cầu đầu tiên tạo biên bản, phân công luật sư, khóa kho và báo Chủ kho,
    /// Người thực thi, luật sư. Lỗi: 404 link sai, 403 sai email, 409 HANDOVER_NOT_ALLOWED.
    /// </summary>
    Task<MyHandoverClaimResponse> SubmitClaimAsync(string token, string? note, List<UploadedFile> files, CancellationToken ct);

    Task<List<MyHandoverClaimResponse>> ListMyClaimsAsync(CancellationToken ct);

    /// <summary>Nộp thêm giấy tờ khi luật sư yêu cầu bổ sung; yêu cầu quay về chờ xét.</summary>
    Task<MyHandoverClaimResponse> SupplementClaimAsync(Guid claimId, string? note, List<UploadedFile> files, CancellationToken ct);

    Task<PreparedDownload> PrepareMyClaimFileAsync(Guid claimId, Guid fileId, CancellationToken ct);

    // Chủ kho

    /// <summary>Yêu cầu nhận tài sản đang mở của kho; null khi không có.</summary>
    Task<OwnerHandoverRequestResponse?> GetOwnerRequestAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Chủ kho hủy yêu cầu trước khi biên bản đủ chữ ký: hủy biên bản và lượt, kho mở khóa.</summary>
    Task CancelByOwnerAsync(Guid vaultId, CancellationToken ct);

    // Người nhận

    /// <summary>Biên bản nhìn từ một lượt của người nhận (chỉ thấy phần của mình).</summary>
    Task<HandoverRecordResponse> GetForRecipientAsync(Guid turnId, CancellationToken ct);

    /// <summary>Người nhận ký biên bản ở bước 3.</summary>
    Task<HandoverRecordResponse> SignAsRecipientAsync(Guid turnId, CancellationToken ct);

    // Dùng nội bộ

    /// <summary>
    /// Gọi khi luật sư vừa xác nhận danh tính một người nhận (trước khi lưu): đủ người thì biên bản sang bước ký.
    /// </summary>
    Task AdvanceAfterIdentityAsync(HandoverTurn turn, DateTimeOffset now, CancellationToken ct);

    /// <summary>Luật sư được phân công cho biên bản của kho; null khi kho chưa mở bàn giao.</summary>
    Task<Guid?> FindVerifierUserIdAsync(Guid vaultId, CancellationToken ct);
}
