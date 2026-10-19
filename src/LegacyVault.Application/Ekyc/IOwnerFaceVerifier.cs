namespace LegacyVault.Application.Ekyc;

/// <summary>Kết quả so selfie của Owner với mẫu khuôn mặt đã lưu.</summary>
public enum OwnerFaceOutcome
{
    /// <summary>Khớp mẫu khuôn mặt của Owner.</summary>
    Matched = 0,
    /// <summary>Không khớp (tính vào số lần thất bại).</summary>
    NotMatched = 1,
    /// <summary>Chưa có mẫu khuôn mặt hoặc dịch vụ so khớp không dùng được: lỗi kỹ thuật, không tính là thất bại (EKYC-19).</summary>
    Unavailable = 2,
}

/// <summary>
/// So selfie trực tiếp của Owner với MẪU KHUÔN MẶT đã tạo từ phiên đăng ký đạt (ALIVE-02, EKYC-22, EKYC-26).
/// Không so với ảnh tải lên hay ảnh giấy tờ cũ. Cài đặt ở Infrastructure.
/// </summary>
/// <remarks>
/// Mẫu là vector mã hóa trong bảng OwnerFaceTemplates, tạo khi Owner hoàn thành thử thách camera của lượt OWNER_ENROLLMENT
/// (EkycService.CompleteLivenessAsync). Owner chưa có mẫu thì trả Unavailable.
/// </remarks>
public interface IOwnerFaceVerifier
{
    Task<OwnerFaceOutcome> VerifyAsync(
        Guid ownerUserId, byte[] selfie, string fileName, string contentType, CancellationToken ct);
}
