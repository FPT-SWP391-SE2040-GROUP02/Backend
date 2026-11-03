using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.ServiceConfig;

/// <summary>
/// Thử gọi một dịch vụ ngoài bằng cấu hình nháp để Admin biết giá trị đang nhập có dùng được không trước khi lưu.
/// Mỗi dịch vụ có một bản cài đặt ở Infrastructure.
/// </summary>
public interface IServiceConnectionTester
{
    /// <summary>Mã dịch vụ mà bản cài đặt này thử (SystemConfigServices.*).</summary>
    string ServiceKey { get; }

    /// <summary>
    /// Gọi nhẹ tới dịch vụ, chờ tối đa 10 giây. Không ném lỗi khi dịch vụ hỏng: trả Ok = false kèm ErrorCode ngắn
    /// (HTTP_401, TIMEOUT, NETWORK...). Kết quả không bao giờ chứa khóa.
    /// </summary>
    Task<TestSystemConfigResponse> TestAsync(ResolvedConfig draft, CancellationToken ct);
}
