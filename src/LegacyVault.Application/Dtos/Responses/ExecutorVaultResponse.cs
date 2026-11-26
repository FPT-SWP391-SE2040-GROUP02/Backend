namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một kho mà người dùng đang là Người thực thi (GET /api/v1/executor/vaults).</summary>
public class ExecutorVaultResponse
{
    public Guid VaultId { get; set; }

    /// <summary>Họ tên Chủ tài sản theo tài khoản, để Người thực thi nhận ra kho.</summary>
    public string OwnerFullName { get; set; } = string.Empty;

    public string PlanStatus { get; set; } = string.Empty;

    /// <summary>Hướng dẫn Chủ tài sản để lại cho Người thực thi; null khi Chủ tài sản chưa viết.</summary>
    public string? ExecutorNote { get; set; }

    public DateTimeOffset? ExecutorNoteUpdatedAt { get; set; }

    /// <summary>Lúc tài khoản chấp nhận lời mời làm Người thực thi của kho; null khi dữ liệu cũ không có.</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>Phiên bản biên bản phân chia Chủ tài sản ký gần nhất; null khi chưa ký. Không kèm tài sản hay người nhận.</summary>
    public int? WillVersionNo { get; set; }

    /// <summary>Lúc Chủ tài sản ký phiên bản đó; null cùng lúc với WillVersionNo.</summary>
    public DateTimeOffset? WillSignedAt { get; set; }

    /// <summary>Phiên bản tôi đã xem và ký gần nhất; null khi chưa ký phiên bản nào.</summary>
    public int? ConfirmedWillVersionNo { get; set; }

    /// <summary>Lúc tôi ký phiên bản đó.</summary>
    public DateTimeOffset? WillConfirmedAt { get; set; }

    /// <summary>Phiên bản tôi đã không đồng ý; null khi không có.</summary>
    public int? DeclinedWillVersionNo { get; set; }

    /// <summary>Kế hoạch đang chạy và Chủ tài sản đã ký phiên bản mới mà tôi chưa trả lời.</summary>
    public bool WillConsentRequired { get; set; }

    /// <summary>Số tài sản trong kho; chỉ là số đếm.</summary>
    public int AssetCount { get; set; }

    /// <summary>Số người nhận trên tờ đã ký gần nhất.</summary>
    public int RecipientCount { get; set; }

    /// <summary>Biên bản bàn giao của kho; null khi chưa mở bàn giao.</summary>
    public Guid? HandoverRecordId { get; set; }

    /// <summary>DRAFTING, IDENTITY_CHECK, SIGNING, COMPLETED; rỗng khi chưa mở bàn giao.</summary>
    public string HandoverRecordStatus { get; set; } = string.Empty;
}
