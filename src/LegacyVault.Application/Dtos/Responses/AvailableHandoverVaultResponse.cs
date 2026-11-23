namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Một kho mà tài khoản đang đăng nhập là người nhận (GET /api/v1/handover/requests/available), kèm bước hiện tại của tôi.
/// Không có tên gói, tên tài sản hay số căn cước.
/// </summary>
public class AvailableHandoverVaultResponse
{
    public Guid VaultId { get; set; }

    /// <summary>Hồ sơ người nhận của tôi trong kho.</summary>
    public Guid BeneficiaryId { get; set; }

    public string OwnerName { get; set; } = string.Empty;

    /// <summary>
    /// NOT_REQUESTED, REQUEST_PENDING, SUPPLEMENT_REQUESTED, REJECTED, IN_REVIEW, NEED_MY_SIGNATURE, WAITING_OTHERS,
    /// CODE_READY, RECEIVED (bảng ánh xạ ở RecipientStepRules).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Tôi cần làm gì đó ở kho này (gửi yêu cầu khi người khác đã gửi, nộp bổ sung, ký, nhập mã và tải).</summary>
    public bool NeedsAction { get; set; }

    /// <summary>Gửi được yêu cầu nhận tài sản lúc này (chỉ khi NOT_REQUESTED).</summary>
    public bool CanRequest { get; set; }

    /// <summary>Lý do luật sư yêu cầu bổ sung hoặc từ chối; null khi không có.</summary>
    public string? ReviewReason { get; set; }

    /// <summary>Yêu cầu nhận của tôi (để nộp bổ sung); null khi chưa gửi.</summary>
    public Guid? ClaimId { get; set; }

    /// <summary>Lượt bàn giao của tôi sau khi biên bản chốt (để vào cổng người nhận); null khi chưa có.</summary>
    public Guid? TurnId { get; set; }
}
