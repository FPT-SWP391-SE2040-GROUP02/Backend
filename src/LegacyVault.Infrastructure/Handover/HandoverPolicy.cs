using LegacyVault.Application.Services;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Đọc công tắc yêu cầu xác minh danh tính từ mục Handover trong cấu hình.</summary>
public sealed class HandoverPolicy : IHandoverPolicy
{
    private readonly IOptions<HandoverSettings> options;

    public HandoverPolicy(IOptions<HandoverSettings> options)
    {
        this.options = options;
    }

    public bool RequireIdentityVerification
    {
        get { return options.Value.RequireIdentityVerification; }
    }
}
