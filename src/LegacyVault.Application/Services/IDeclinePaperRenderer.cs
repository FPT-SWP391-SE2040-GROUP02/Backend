namespace LegacyVault.Application.Services;

/// <summary>Dữ liệu in lên văn bản từ chối hoặc biên bản đồng ý nhận (IsAcceptance); chỉ gồm thông tin hệ thống đã giữ, không có số căn cước đầy đủ.</summary>
public sealed class DeclinePaperData
{
    /// <summary>true: biên bản đồng ý nhận phần di sản; false: văn bản từ chối.</summary>
    public bool IsAcceptance { get; set; }

    public string TurnCode { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public string OwnerFullName { get; set; } = string.Empty;

    /// <summary>Họ tên người từ chối lấy từ hồ sơ người được chỉ định mà Chủ kho đã khai.</summary>
    public string DeclinerFullName { get; set; } = string.Empty;

    public string CitizenIdLast4 { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; set; }

    public bool NameMatches { get; set; }

    public bool CitizenIdMatches { get; set; }

    public bool DateOfBirthMatches { get; set; }

    public string ReviewerFullName { get; set; } = string.Empty;

    public string? ReviewerLawyerCardNumber { get; set; }

    public DateTimeOffset ReviewedAt { get; set; }
}

/// <summary>Dựng ảnh PNG "Văn bản từ chối nhận di sản" (một trang); cài đặt cụ thể nằm ở Infrastructure.</summary>
public interface IDeclinePaperRenderer
{
    byte[] Render(DeclinePaperData data);
}
