using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Đóng biên bản sau khi luật sư từ chối. Dùng chung cho xét yêu cầu nhận tài sản và xét hồ sơ báo tin để cả hai đường
/// xét cùng một điều kiện (HandoverRecordRules.ShouldCloseAfterAllRejected).
/// </summary>
public static class HandoverRecordClosing
{
    /// <summary>
    /// Biên bản đang lập, kho chưa khóa, mọi yêu cầu đã bị từ chối và không còn hồ sơ báo tin nào của kho chờ xét thì
    /// đóng. rejectedReportId là hồ sơ đang bị từ chối trong lần này (chưa lưu nên phải bỏ ra khi đếm); null khi đang xét
    /// yêu cầu. Chưa lưu.
    /// </summary>
    public static async Task CloseIfNothingLeftAsync(
        IHandoverReportRepository reports, HandoverRecord record, PlanStatus planStatus, Guid? rejectedReportId,
        DateTimeOffset now, CancellationToken ct)
    {
        int pendingReports = await reports.CountPendingByVaultAsync(record.VaultId, rejectedReportId, ct);
        if (HandoverRecordRules.ShouldCloseAfterAllRejected(record, planStatus, pendingReports))
        {
            HandoverRecordRules.CloseAfterAllRejected(record, now);
        }
    }
}
