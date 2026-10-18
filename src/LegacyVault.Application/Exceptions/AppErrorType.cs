namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Loại lỗi nghiệp vụ. Api ánh xạ sang HTTP status; Application không phụ thuộc ASP.NET.
/// </summary>
public enum AppErrorType
{
    /// <summary>400.</summary>
    Validation,
    /// <summary>401.</summary>
    Unauthorized,
    /// <summary>403.</summary>
    Forbidden,
    /// <summary>404.</summary>
    NotFound,
    /// <summary>409.</summary>
    Conflict,
    /// <summary>413.</summary>
    PayloadTooLarge,
    /// <summary>503: dịch vụ ngoài (ví dụ eKYC) không gọi được.</summary>
    ServiceUnavailable,
}
