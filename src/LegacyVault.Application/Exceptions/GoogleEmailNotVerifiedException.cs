namespace LegacyVault.Application.Exceptions;

/// <summary>403: Google chưa xác minh email của tài khoản nên không tạo tài khoản mới từ nó.</summary>
public sealed class GoogleEmailNotVerifiedException : ApiException
{
    public GoogleEmailNotVerifiedException()
        : base(403, "GOOGLE_EMAIL_NOT_VERIFIED", "Email của tài khoản Google này chưa được Google xác minh nên chưa thể đăng ký. Hãy xác minh email trong Google hoặc đăng ký bằng email và mật khẩu.")
    {
    }
}
