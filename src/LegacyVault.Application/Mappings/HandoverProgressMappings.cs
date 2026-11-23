using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

/// <summary>Đổi lượt bàn giao sang dữ liệu tiến độ cho Executor. Không chép nội dung hay thông tin liên hệ (tên, số điện thoại do HandoverProgressService điền riêng khi đã bàn giao).</summary>
public static class HandoverProgressMappings
{
    /// <summary>Một lượt bàn giao trên màn tiến độ: trạng thái, các mốc thời gian và danh sách tài sản theo thứ tự ghim, mỗi tài sản kèm lần tải đầu (nếu lượt đã có quyền tải). citizenId là số căn cước đầy đủ của người nhận; firstDownloads cho biết lần tải đầu của từng tài sản.</summary>
    public static HandoverProgressTurnResponse ToProgressResponse(
        this HandoverTurn turn, string citizenId, Dictionary<Guid, DateTimeOffset> firstDownloads)
    {
        HandoverProgressTurnResponse response = new HandoverProgressTurnResponse
        {
            TurnId = turn.Id,
            ContainerId = turn.ContainerId,
            ContainerName = turn.ContainerName,
            BeneficiaryId = turn.BeneficiaryId,
            BeneficiaryCitizenId = citizenId,
            Status = turn.Status.ToApiString(),
            InvitedAt = turn.InvitedAt,
            InvitationSentAt = turn.InvitationSentAt,
            KeyVerifiedAt = turn.KeyVerifiedAt,
            IdentityConfirmedAt = turn.IdentityConfirmedAt,
            ResponseDeadline = turn.ResponseDeadline,
            ClaimedAt = turn.ClaimedAt,
            IdentityConfirmedByExecutor = turn.IdentityConfirmedByUserId != null,
            DecisionDeadline = turn.DecisionDeadline,
            DecidedAt = turn.DecidedAt,
            ReceivedAllAt = turn.ReceivedAllAt,
            ReceiptExecutorSignedAt = turn.ReceiptExecutorSignedAt,
            ReceiptIssueNote = turn.ReceiptIssueNote,
            ReceiptIssueAt = turn.ReceiptIssueAt
        };
        if (turn.Grant != null)
        {
            response.GrantStatus = turn.Grant.Status.ToApiString();
            response.GrantExpiresAt = turn.Grant.ExpiresAt;
        }

        List<HandoverSnapshotItem> items = new List<HandoverSnapshotItem>(turn.Items);
        items.Sort(CompareByPosition);
        foreach (HandoverSnapshotItem item in items)
        {
            HandoverProgressItemResponse itemResponse = new HandoverProgressItemResponse
            {
                ItemId = item.Id,
                AssetId = item.AssetId,
                Name = item.Name,
                Kind = item.Kind.ToApiString()
            };
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

    /// <summary>So sánh hai tài sản theo thứ tự ghim để sắp danh sách.</summary>
    private static int CompareByPosition(HandoverSnapshotItem left, HandoverSnapshotItem right)
    {
        return left.Position.CompareTo(right.Position);
    }
}
