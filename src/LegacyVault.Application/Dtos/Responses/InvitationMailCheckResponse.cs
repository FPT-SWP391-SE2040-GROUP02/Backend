namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả kiểm thư mời bằng mã hồ sơ và 4 số cuối căn cước (công khai).</summary>
public class InvitationMailCheckResponse
{
    /// <summary>true khi mã hồ sơ và 4 số cuối căn cước khớp lượt mời này.</summary>
    public bool Verified { get; set; }

    /// <summary>Số lần thử còn lại; về 0 thì lần sau nhận 423.</summary>
    public int AttemptsLeft { get; set; }

    /// <summary>Tiêu đề lượt bàn giao; chỉ có khi Verified.</summary>
    public string? ContainerName { get; set; }

    /// <summary>Họ tên Chủ kho; chỉ có khi Verified.</summary>
    public string? OwnerFullName { get; set; }
}
