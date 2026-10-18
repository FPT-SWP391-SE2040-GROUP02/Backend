namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Tạo lỗi 400 VALIDATION_ERROR cùng dạng với lỗi model binding: <c>errors</c> là object theo tên trường (camelCase).
/// Dùng cho luật mà DataAnnotations không diễn tả được (ví dụ tên chỉ toàn khoảng trắng).
/// </summary>
public static class ValidationErrors
{
    public static AppException For(string field, string message)
    {
        return new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
            "Dữ liệu gửi lên không hợp lệ.",
            new Dictionary<string, object?>
            {
                ["errors"] = new Dictionary<string, string[]> { [field] = [message] },
            });
    }
}
