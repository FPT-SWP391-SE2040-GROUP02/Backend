namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Lớp cơ sở cho mọi ngoại lệ nghiệp vụ trả mã lỗi theo RFC 9457 (docs/api-contract.md).
/// </summary>
public abstract class ApiException : Exception
{
    protected ApiException(int statusCode, string errorCode, string message) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public string ErrorCode { get; }
    public Dictionary<string, object?> ExtraData { get; } = new();
}
