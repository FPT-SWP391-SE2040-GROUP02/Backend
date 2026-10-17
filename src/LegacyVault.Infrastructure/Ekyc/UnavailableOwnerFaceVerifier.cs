using LegacyVault.Application.Ekyc;

namespace LegacyVault.Infrastructure.Ekyc;

/// <summary>
/// Bản mặc định: chưa có nơi lưu mẫu khuôn mặt Owner (LV-02) nên luôn báo không dùng được.
/// Selfie bị xem là lỗi kỹ thuật (503), không tính là thất bại; Verifier vẫn kết luận được sau 7 ngày.
/// </summary>
public sealed class UnavailableOwnerFaceVerifier : IOwnerFaceVerifier
{
    public Task<OwnerFaceOutcome> VerifyAsync(
        Guid ownerUserId, byte[] selfie, string fileName, string contentType, CancellationToken ct)
    {
        return Task.FromResult(OwnerFaceOutcome.Unavailable);
    }
}
