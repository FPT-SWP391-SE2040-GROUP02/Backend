using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

/// <summary>Đổi tài sản đã ghim của lượt bàn giao sang dữ liệu trả cho người nhận. Chỉ có tên và thông tin tệp, không có nội dung.</summary>
public static class HandoverMappings
{
    public static HandoverItemResponse ToResponse(this HandoverSnapshotItem item)
    {
        HandoverItemResponse response = new HandoverItemResponse();
        response.Id = item.Id;
        response.Name = item.Name;
        response.Kind = item.Kind.ToApiString();
        response.FileName = item.FileName;
        response.ContentType = item.ContentType;
        response.SizeBytes = item.SizeBytes;
        return response;
    }
}
