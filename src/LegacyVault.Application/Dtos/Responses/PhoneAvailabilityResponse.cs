namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Số điện thoại (đã chuẩn hóa) có còn dùng được cho tài khoản mới không.</summary>
public sealed class PhoneAvailabilityResponse
{
    public bool Available { get; set; }
}
