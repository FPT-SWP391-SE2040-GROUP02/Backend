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

    /// <summary>
    /// Mở bàn giao: tạo biên bản, phân công một luật sư, khóa cấu hình kho.
    /// Lỗi: 404 VAULT_NOT_FOUND, 409 HANDOVER_NOT_ALLOWED (kho chưa kích hoạt, đã mở, chưa có luật sư nào nhận được).
    /// </summary>
    Task<HandoverRecordResponse> OpenAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Người thực thi ký biên bản ở bước 3.</summary>
    Task<HandoverRecordResponse> SignAsExecutorAsync(Guid vaultId, CancellationToken ct);

    // Luật sư

    Task<List<HandoverRecordSummaryResponse>> ListForVerifierAsync(CancellationToken ct);
    Task<HandoverRecordResponse> GetForVerifierAsync(Guid recordId, CancellationToken ct);

    /// <summary>Lấy file phân chia của Chủ kho (các gói sẵn sàng) làm bản đầu, thay toàn bộ dòng đang có.</summary>
    Task<HandoverRecordResponse> UseOwnerSplitAsync(Guid recordId, CancellationToken ct);

    /// <summary>Thay toàn bộ dòng "ai nhận tài sản nào". Chỉ khi biên bản đang lập (bước 1).</summary>
    Task<HandoverRecordResponse> SetLinesAsync(Guid recordId, SetHandoverRecordLinesRequest request, CancellationToken ct);

    /// <summary>Chốt biên bản (xong bước 1): tạo lượt cho từng người nhận và gửi thư mời.</summary>
    Task<HandoverRecordResponse> FinalizeAsync(Guid recordId, CancellationToken ct);

    /// <summary>Mở lại biên bản đã chốt để sửa: mọi chữ ký cũ mất hiệu lực.</summary>
    Task<HandoverRecordResponse> ReopenAsync(Guid recordId, CancellationToken ct);

    /// <summary>Luật sư ký xác minh ở bước 3.</summary>
    Task<HandoverRecordResponse> SignAsVerifierAsync(Guid recordId, CancellationToken ct);

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
