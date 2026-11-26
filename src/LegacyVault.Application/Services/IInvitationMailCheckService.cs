using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Kiểm thư mời có thật không bằng mã hồ sơ và số căn cước ghi trong thư, để chống thư giả. Không ghi gì.</summary>
public interface IInvitationMailCheckService
{
    /// <summary>
    /// Công khai (không cần đăng nhập): người nhận nhập mã hồ sơ và số căn cước ghi trong thư để biết thư có thật không.
    /// Đúng thì trả tên gói và tên Chủ tài sản; sai thì trả số lần thử còn lại. Hết lượt thì 423 HANDOVER_MAIL_CHECK_LOCKED.
    /// </summary>
    Task<InvitationMailCheckResponse> CheckAsync(string token, string caseCode, string citizenId, CancellationToken ct);
}
