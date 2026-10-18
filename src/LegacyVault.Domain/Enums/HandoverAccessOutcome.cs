namespace LegacyVault.Domain.Enums;

/// <summary>Kết quả một lần yêu cầu tải nội dung, ghi vào nhật ký truy cập.</summary>
public enum HandoverAccessOutcome
{
    /// <summary>Đã qua kiểm quyền và kiểm toàn vẹn; nội dung được trả.</summary>
    Succeeded = 0,
    /// <summary>Grant không còn hiệu lực hoặc kế hoạch không cho phép.</summary>
    Denied = 1,
    /// <summary>Sai tag, sai checksum hoặc sai kích thước: không trả byte nội dung nào (DOWNLOAD-05).</summary>
    IntegrityFailed = 2,
}
