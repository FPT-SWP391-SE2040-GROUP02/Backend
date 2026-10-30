namespace LegacyVault.Application.Notifications;

/// <summary>Loại email báo cho Owner về mã PIN của kho.</summary>
public enum VaultPinNoticeKind
{
    /// <summary>PIN vừa được đặt lần đầu.</summary>
    Set,
    /// <summary>PIN vừa được đổi bằng PIN cũ.</summary>
    Changed,
    /// <summary>PIN vừa được đặt lại bằng mật khẩu tài khoản (quên PIN).</summary>
    Reset,
    /// <summary>PIN bị khóa tạm vì nhập sai nhiều lần.</summary>
    Locked,
}
