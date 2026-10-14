namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Lỗi nghiệp vụ có mã ổn định cho FE (docs/api-contract.md mục 2).
/// GlobalExceptionHandler chuyển thành ProblemDetails: status theo Type, title = Message,
/// code = Code (UPPER_SNAKE_CASE), các phần tử của Extensions thành trường phụ ở cấp cao nhất
/// (giá trị enum cũng đổi sang UPPER_SNAKE_CASE).
/// </summary>
/// <remarks>Message là câu hiển thị cho người dùng; không đưa dữ liệu nhạy cảm/nội bộ vào đây.</remarks>
public class AppException : Exception
{
    public AppException(AppErrorType type, ErrorCode code, string message,
        IReadOnlyDictionary<string, object?>? extensions = null)
        : base(message)
    {
        Type = type;
        Code = code;
        Extensions = extensions ?? new Dictionary<string, object?>();
    }

    public AppErrorType Type { get; }

    public ErrorCode Code { get; }

    public IReadOnlyDictionary<string, object?> Extensions { get; }
}
