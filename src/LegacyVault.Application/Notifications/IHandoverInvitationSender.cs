namespace LegacyVault.Application.Notifications;

/// <summary>Dữ liệu một lời mời nhận tài sản. Token gốc chỉ đi qua đây, không bao giờ lưu DB (chỉ lưu hash).</summary>
public sealed class HandoverInvitation
{
    public Guid TurnId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Token gốc để dựng link mời.</summary>
    public string Token { get; set; } = string.Empty;

    public DateTimeOffset ResponseDeadline { get; set; }

    /// <summary>Họ tên Chủ kho (đã mất) để thư nhắc lại ai đã chọn người nhận. Rỗng thì thư dùng cách nói chung.</summary>
    public string OwnerFullName { get; set; } = string.Empty;

    /// <summary>Người thực thi hiện tại của kho, để người nhận gọi hỏi được. Rỗng thì thư bỏ phần liên hệ này.</summary>
    public string ExecutorFullName { get; set; } = string.Empty;

    public string? ExecutorPhone { get; set; }

    public string ExecutorEmail { get; set; } = string.Empty;

    /// <summary>Mã hồ sơ chứng tử (8 ký tự), để người nhận kiểm thư trên trang lời mời.</summary>
    public string CaseCode { get; set; } = string.Empty;

    /// <summary>Họ tên và số thẻ luật sư của Người xét duyệt đã duyệt hồ sơ; rỗng khi chưa có.</summary>
    public string VerifierFullName { get; set; } = string.Empty;

    public string? VerifierLawyerCardNumber { get; set; }

    /// <summary>Lời nhắn Chủ kho để lại cho người nhận (không bắt buộc). Null thì thư bỏ khung này.</summary>
    public string? OwnerMessage { get; set; }

    /// <summary>Điền các thông tin liên hệ của kho vào lời mời; thiếu thông tin thì giữ nguyên giá trị rỗng.</summary>
    public void ApplyContacts(HandoverContactInfo? contacts)
    {
        if (contacts == null)
        {
            return;
        }
        OwnerFullName = contacts.OwnerFullName;
        ExecutorFullName = contacts.ExecutorFullName;
        ExecutorPhone = contacts.ExecutorPhone;
        ExecutorEmail = contacts.ExecutorEmail;
        CaseCode = contacts.CaseCode;
        VerifierFullName = contacts.VerifierFullName;
        VerifierLawyerCardNumber = contacts.VerifierLawyerCardNumber;
    }
}

/// <summary>Thông tin liên hệ của một kho dùng trong thư mời nhận tài sản.</summary>
public sealed class HandoverContactInfo
{
    public string OwnerFullName { get; set; } = string.Empty;

    public string ExecutorFullName { get; set; } = string.Empty;

    public string? ExecutorPhone { get; set; }

    public string ExecutorEmail { get; set; } = string.Empty;

    public string CaseCode { get; set; } = string.Empty;

    public string VerifierFullName { get; set; } = string.Empty;

    public string? VerifierLawyerCardNumber { get; set; }
}

/// <summary>Đọc họ tên Chủ kho và liên hệ Người thực thi của kho. Chỉ đọc; không có nội dung tài sản.</summary>
public interface IHandoverContactReader
{
    /// <summary>Kho không có Chủ kho hoặc chưa có Người thực thi đang hiệu lực thì trường tương ứng để rỗng.</summary>
    Task<HandoverContactInfo> FindAsync(Guid vaultId, CancellationToken ct);
}

/// <summary>
/// Gửi lời mời cho người nhận (INVITE-01). Cài đặt ở Infrastructure (email).
/// </summary>
/// <remarks>
/// OPS-04: email chỉ có tên và link, không có tên gói hay tên tài sản.
/// Gửi lỗi thì ném exception; HandoverStarter giữ lượt và để InvitationSentAt = null.
/// </remarks>
public interface IHandoverInvitationSender
{
    Task SendAsync(HandoverInvitation invitation, CancellationToken ct);
}
