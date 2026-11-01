using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

/// <summary>Đổi lượt bàn giao sang dữ liệu tiến độ cho Executor. Không chép nội dung hay thông tin liên hệ.</summary>
public static class HandoverProgressMappings
{
    public static HandoverProgressTurnResponse ToProgressResponse(
        this HandoverTurn turn, string citizenIdLast4, Dictionary<Guid, DateTimeOffset> firstDownloads)
    {
        HandoverProgressTurnResponse response = new HandoverProgressTurnResponse();
        response.TurnId = turn.Id;
        response.ContainerName = turn.ContainerName;
        response.BeneficiaryCitizenIdLast4 = citizenIdLast4;
        response.Status = turn.Status.ToApiString();
        response.InvitedAt = turn.InvitedAt;
        response.ResponseDeadline = turn.ResponseDeadline;
        response.ClaimedAt = turn.ClaimedAt;
        response.IdentityConfirmedByExecutor = turn.IdentityConfirmedByUserId != null;
        response.DecisionDeadline = turn.DecisionDeadline;
        response.DecidedAt = turn.DecidedAt;
        if (turn.Grant != null)
        {
            response.GrantStatus = turn.Grant.Status.ToApiString();
            response.GrantExpiresAt = turn.Grant.ExpiresAt;
        }

        List<HandoverSnapshotItem> items = new List<HandoverSnapshotItem>(turn.Items);
        items.Sort(CompareByPosition);
        foreach (HandoverSnapshotItem item in items)
        {
            HandoverProgressItemResponse itemResponse = new HandoverProgressItemResponse();
            itemResponse.ItemId = item.Id;
            itemResponse.Name = item.Name;
            itemResponse.Kind = item.Kind.ToApiString();
            // Chỉ lượt đã có quyền tải mới có thể có lần tải; tra theo id tài sản đã ghim của lượt này.
            if (turn.Grant != null && firstDownloads.ContainsKey(item.Id))
            {
                itemResponse.Downloaded = true;
                itemResponse.FirstDownloadedAt = firstDownloads[item.Id];
                response.DownloadedItems++;
            }
            response.Items.Add(itemResponse);
        }
        response.TotalItems = response.Items.Count;
        return response;
    }

    private static int CompareByPosition(HandoverSnapshotItem left, HandoverSnapshotItem right)
    {
        return left.Position.CompareTo(right.Position);
    }
}
