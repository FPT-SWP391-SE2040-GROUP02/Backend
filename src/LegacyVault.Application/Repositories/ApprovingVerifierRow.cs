namespace LegacyVault.Application.Repositories;

/// <summary>Họ tên và số thẻ luật sư của Người xét duyệt, để hiện trên màn người nhận.</summary>
public class ApprovingVerifierRow
{
    public string FullName { get; set; } = string.Empty;

    public string? LawyerCardNumber { get; set; }
}
