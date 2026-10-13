namespace LegacyVault.Domain.Enums;

/// <summary>Trạng thái quyền dịch vụ cấp kho (SRS §11.1). Chỉ đổi qua nghiệp vụ thanh toán (§11.6).</summary>
public enum EntitlementStatus
{
    /// <summary>Hạng Free, chưa trả phí.</summary>
    Free = 0,
    /// <summary>Đã trả phí và còn trong kỳ.</summary>
    PaidActive = 1,
    /// <summary>Hết kỳ, đang ân hạn 180 ngày: chặn thêm, cho thao tác giảm (OPLAN-01).</summary>
    GraceReadOnly = 2,
    /// <summary>Hết ân hạn, chờ rà soát; không tự xóa.</summary>
    ExpiredReview = 3,
}
