using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Phía người nhận: kiểm link, nhận lượt, xem, đồng ý/từ chối, liệt kê tài sản (SRS §6.3).
/// Lượt/grant/tài sản không phải của người đang đăng nhập đều trả cùng một 404 HANDOVER_NOT_FOUND (SEC-04).
/// </summary>
public interface IHandoverService
{
    /// <summary>Công khai. Token lạ → 404 HANDOVER_INVITATION_INVALID.</summary>
    Task<InvitationStatusResponse> GetInvitationAsync(string token, CancellationToken ct);

    /// <summary>Gắn tài khoản đang đăng nhập vào lượt. Gọi lặp bởi cùng người là an toàn.</summary>
    Task<HandoverTurnResponse> ClaimAsync(string token, CancellationToken ct);

    Task<List<HandoverTurnResponse>> GetMyTurnsAsync(CancellationToken ct);

    Task<HandoverTurnResponse> GetTurnAsync(Guid turnId, CancellationToken ct);

    /// <summary>Đồng ý nhận: tạo grant 30 ngày. Gọi lặp trả lại cùng grant.</summary>
    Task<HandoverTurnResponse> AcceptAsync(Guid turnId, CancellationToken ct);

    Task<HandoverTurnResponse> DeclineAsync(Guid turnId, CancellationToken ct);

    Task<List<HandoverItemResponse>> ListAssetsAsync(Guid grantId, CancellationToken ct);
}
