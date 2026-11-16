using System.Runtime.CompilerServices;

namespace LegacyVault.IntegrationTests.TestSupport;

internal static class TestEnvironment
{
    /// <summary>Email dùng trong test là địa chỉ giả; tắt tra DNS để test không phụ thuộc mạng.</summary>
    [ModuleInitializer]
    internal static void DisableEmailDomainCheck()
    {
        Environment.SetEnvironmentVariable("Email__DomainCheckEnabled", "false");
    }
}
