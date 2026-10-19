namespace LegacyVault.Application.Exceptions;

/// <summary>Lỗi xác thực hai bước của Admin (api-contract mục 6.2a). Mã lỗi ổn định cho FE.</summary>
public sealed class TwoFactorException : ApiException
{
    private TwoFactorException(int statusCode, string errorCode, string message)
        : base(statusCode, errorCode, message)
    {
    }

    /// <summary>401: sai mã. Phiên đăng nhập (nếu có) vẫn còn.</summary>
    public static TwoFactorException InvalidCode()
    {
        return new TwoFactorException(401, "INVALID_TWO_FACTOR_CODE", "Mã xác thực không đúng hoặc đã được dùng.");
    }

    /// <summary>401: thử thách không tồn tại, hết hạn, đã dùng hoặc bị hủy do sai quá nhiều lần.</summary>
    public static TwoFactorException InvalidChallenge()
    {
        return new TwoFactorException(401, "TWO_FACTOR_CHALLENGE_INVALID",
            "Phiên xác thực hai bước không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.");
    }

    /// <summary>403: chỉ tài khoản Admin dùng được xác thực hai bước.</summary>
    public static TwoFactorException AdminOnly()
    {
        return new TwoFactorException(403, "TWO_FACTOR_ADMIN_ONLY", "Chỉ tài khoản quản trị mới dùng xác thực hai bước.");
    }

    /// <summary>409: đã bật rồi.</summary>
    public static TwoFactorException AlreadyEnabled()
    {
        return new TwoFactorException(409, "TWO_FACTOR_ALREADY_ENABLED", "Xác thực hai bước đã được bật.");
    }

    /// <summary>409: chưa thiết lập hoặc chưa bật.</summary>
    public static TwoFactorException NotEnabled()
    {
        return new TwoFactorException(409, "TWO_FACTOR_NOT_ENABLED", "Xác thực hai bước chưa được bật.");
    }

    /// <summary>409: chưa gọi setup trước khi bật.</summary>
    public static TwoFactorException SetupRequired()
    {
        return new TwoFactorException(409, "TWO_FACTOR_SETUP_REQUIRED", "Cần thiết lập khóa xác thực trước khi bật.");
    }
}
