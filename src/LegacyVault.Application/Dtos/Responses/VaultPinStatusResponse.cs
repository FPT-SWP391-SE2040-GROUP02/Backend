namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Trạng thái mã PIN của kho. Không bao giờ chứa PIN hay hash.</summary>
public class VaultPinStatusResponse
{
    /// <summary>Kho đã đặt PIN chưa.</summary>
    public bool IsSet { get; set; }

    /// <summary>true khi kho đã có tài sản: PIN là bắt buộc (FE hiện hộp thoại đặt PIN khi IsRequired mà chưa IsSet).</summary>
    public bool IsRequired { get; set; }

    /// <summary>Hết khóa lúc nào; null khi không bị khóa.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>Còn mấy lần nhập sai trước khi bị khóa; null khi chưa đặt PIN.</summary>
    public int? AttemptsRemaining { get; set; }
}
