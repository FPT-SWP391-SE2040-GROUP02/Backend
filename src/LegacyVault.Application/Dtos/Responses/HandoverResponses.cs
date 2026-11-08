namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả kiểm thư mời bằng mã hồ sơ và 4 số cuối căn cước (công khai).</summary>
public class InvitationMailCheckResponse
{
    /// <summary>true khi mã hồ sơ và 4 số cuối căn cước khớp lượt mời này.</summary>
    public bool Verified { get; set; }

    /// <summary>Số lần thử còn lại; về 0 thì lần sau nhận 423.</summary>
    public int AttemptsLeft { get; set; }

    /// <summary>Tên gói được chỉ định; chỉ có khi Verified.</summary>
    public string? ContainerName { get; set; }

    /// <summary>Họ tên Chủ kho; chỉ có khi Verified.</summary>
    public string? OwnerFullName { get; set; }
}

/// <summary>Kết quả kiểm link mời (công khai): chỉ hai cờ, không lộ thêm dữ liệu nào (SEC-04).</summary>
public class InvitationStatusResponse
{
    public bool Valid { get; set; }

    public bool ResponseExpired { get; set; }
}

public class HandoverItemResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>FILE hoặc TEXT.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? FileName { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
}

public class HandoverGrantResponse
{
    public Guid Id { get; set; }

    /// <summary>ACTIVE, BLOCKED, EXPIRED, REVOKED (đã tính cả trường hợp quá hạn mà job chưa chạy).</summary>
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}

public class HandoverContainerResponse
{
    public string Name { get; set; } = string.Empty;
}

public class HandoverTurnResponse
{
    public Guid Id { get; set; }

    /// <summary>Tên trạng thái UPPER_SNAKE: INVITED, IDENTITY_IN_PROGRESS, READY_TO_ACCEPT, ...</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Giờ máy chủ, để trang web đếm ngược không phụ thuộc đồng hồ máy người dùng.</summary>
    public DateTimeOffset ServerTime { get; set; }

    public DateTimeOffset InvitedAt { get; set; }

    public DateTimeOffset ResponseDeadline { get; set; }

    public DateTimeOffset? DecisionDeadline { get; set; }

    /// <summary>true: xác minh danh tính đã được bỏ qua bằng công tắc cấu hình (chỉ Development).</summary>
    public bool IdentityBypassed { get; set; }

    /// <summary>true: một Executor đã xác nhận đúng người bằng tay thay cho eKYC (BR-122).</summary>
    public bool IdentityConfirmedByExecutor { get; set; }

    /// <summary>true: kho đã cấp khóa mở gói, người nhận phải nhập khóa do Người thực hiện đưa trước khi đồng ý.</summary>
    public bool KeyRequired { get; set; }

    /// <summary>true: người nhận đã nhập đúng khóa (hoặc không cần khóa).</summary>
    public bool KeyVerified { get; set; }

    /// <summary>Chỉ có khi lượt ở READY_TO_ACCEPT hoặc HANDOVER_COMMITTED.</summary>
    public HandoverContainerResponse? Container { get; set; }

    /// <summary>Chỉ có khi lượt ở READY_TO_ACCEPT hoặc HANDOVER_COMMITTED; ngược lại rỗng.</summary>
    public List<HandoverItemResponse> Items { get; set; } = new List<HandoverItemResponse>();

    public HandoverGrantResponse? Grant { get; set; }
}
