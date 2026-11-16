using System.Net;
using System.Net.Sockets;
using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Tra DNS của tên miền email; chỉ HostNotFound (NXDOMAIN) mới bị coi là không tồn tại.</summary>
/// <remarks>Cấu hình Email:DomainCheckEnabled (mặc định true); test tích hợp tắt đi để không phụ thuộc mạng.</remarks>
public sealed class DnsEmailDomainChecker(IConfiguration config) : IEmailDomainChecker
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<bool> DomainExistsAsync(string email, CancellationToken ct)
    {
        if (!config.GetValue("Email:DomainCheckEnabled", true))
        {
            return true;
        }
        int at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1)
        {
            return true; // sai định dạng đã có bước kiểm tra riêng
        }
        string domain = email[(at + 1)..].Trim().TrimEnd('.');
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            await Dns.GetHostEntryAsync(domain, timeout.Token);
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.HostNotFound)
        {
            return false;
        }
        catch (SocketException)
        {
            return true; // NoData, TryAgain...: không chắc thì cho qua
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return true; // DNS chậm
        }
    }
}
