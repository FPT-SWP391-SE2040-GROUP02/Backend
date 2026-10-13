namespace LegacyVault.Application.Security;

/// <summary>
/// Người đang gọi API, đọc từ token đăng nhập đã được xác thực. Cài đặt ở Infrastructure/Api.
/// </summary>
/// <remarks>KHÔNG BAO GIỜ nhận userId từ body/query của FE (SEC-04).</remarks>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }
}
