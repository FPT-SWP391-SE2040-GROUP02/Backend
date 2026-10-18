namespace LegacyVault.Application.Services;

/// <summary>
/// Che bớt email trước khi trả cho FE, ví dụ "an.nguyen@gmail.com" → "an***@gmail.com".
/// </summary>
/// <remarks>
/// Dùng ở màn hình "nhập mật khẩu để liên kết Google": đủ để người dùng nhận ra tài khoản của mình,
/// nhưng không in nguyên email ra màn hình.
/// </remarks>
public static class EmailMasker
{
    public static string Mask(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
        {
            return "***";
        }

        var localPart = email.Substring(0, atIndex);
        var domain = email.Substring(atIndex);

        // Giữ 2 ký tự đầu; phần tên quá ngắn thì chỉ giữ 1 ký tự.
        var visibleCount = localPart.Length > 2 ? 2 : 1;
        return localPart.Substring(0, visibleCount) + "***" + domain;
    }
}
