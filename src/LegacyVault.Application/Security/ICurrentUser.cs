namespace LegacyVault.Application.Security;

/// <summary>
/// Người đang gọi API, đọc từ token đăng nhập đã được xác thực. Cài đặt ở Infrastructure/Api.
/// </summary>
/// <remarks>KHÔNG BAO GIỜ nhận userId từ body/query của FE (SEC-04).</remarks>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    /// <summary>Phiên (family) của access token hiện tại; null nếu token không mang claim này.</summary>
    Guid? SessionFamilyId => null;

    /// <summary>Dải IP người gọi (IPv4 /24, IPv6 /64) để đếm sai mật khẩu; "unknown" nếu không rõ.</summary>
    string ClientIpRange => "unknown";
}
