using System.Runtime.CompilerServices;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Tắt job nền bàn giao trong mọi test dùng API thật, để job không chạy xen vào các bước kiểm thử.
/// Dùng ModuleInitializer thay vì sửa ApiFactory (file dùng chung với các nhánh khác).
/// </summary>
internal static class HandoverTestEnvironment
{
    [ModuleInitializer]
    internal static void DisableHandoverJob()
    {
        Environment.SetEnvironmentVariable("Handover__Job__Enabled", "false");
    }
}
