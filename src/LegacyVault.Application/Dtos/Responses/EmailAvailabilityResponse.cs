namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Email (đã chuẩn hóa) có còn dùng được cho tài khoản mới không.</summary>
public sealed class EmailAvailabilityResponse
{
    public bool Available { get; set; }
}
