using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Luật nghiệp vụ của gói (Container) dùng chung cho các service.</summary>
/// <remarks>
/// recipientCount là số chỉ định người nhận hợp lệ của gói. Chưa có module người nhận nên hiện luôn truyền 0;
/// nhánh người nhận sẽ truyền số thật.
/// </remarks>
public static class ContainerRules
{
    /// <summary>NFR-14: tối đa 50 gói chưa lưu trữ mỗi kho.</summary>
    public const int MaxActiveContainers = 50;

    public const string NoAsset = "NO_ASSET";
    public const string NoDesignation = "NO_DESIGNATION";

    /// <summary>
    /// SETUP-16: backend tự tính DRAFT/READY. READY cần ≥1 tài sản VÀ ≥1 chỉ định hợp lệ.
    /// Gói ARCHIVED do Owner chọn nên giữ nguyên.
    /// </summary>
    public static void RecalculateStatus(Container container, int recipientCount)
    {
        if (container.Status == ContainerStatus.Archived)
        {
            return;
        }

        if (container.Assets.Count > 0 && recipientCount > 0)
        {
            container.Status = ContainerStatus.Ready;
        }
        else
        {
            container.Status = ContainerStatus.Draft;
        }
    }

    /// <summary>Lý do gói còn DRAFT để FE hướng dẫn. Gói READY/ARCHIVED → danh sách rỗng.</summary>
    public static List<string> GetMissingRequirements(Container container, int recipientCount)
    {
        List<string> missing = new List<string>();
        if (container.Status == ContainerStatus.Ready || container.Status == ContainerStatus.Archived)
        {
            return missing;
        }

        if (container.Assets.Count == 0)
        {
            missing.Add(NoAsset);
        }
        if (recipientCount == 0)
        {
            missing.Add(NoDesignation);
        }
        return missing;
    }
}
