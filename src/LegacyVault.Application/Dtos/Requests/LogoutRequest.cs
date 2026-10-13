namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body (không bắt buộc) của POST /api/v1/auth/logout.</summary>
public class LogoutRequest
{
    /// <summary>true = thu hồi phiên trên mọi thiết bị của tài khoản.</summary>
    public bool RevokeAllDevices { get; set; }
}
