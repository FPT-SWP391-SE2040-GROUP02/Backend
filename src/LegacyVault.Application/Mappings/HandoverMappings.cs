using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

/// <summary>Đổi tài sản đã ghim của lượt bàn giao sang dữ liệu trả cho người nhận. Chỉ có tên và thông tin tệp, không có nội dung.</summary>
public static class HandoverMappings
{
    /// <summary>Một tài sản đã ghim của lượt: tên, loại, tên tệp, loại tệp và kích thước.</summary>
    public static HandoverItemResponse ToResponse(this HandoverSnapshotItem item)
    {
        HandoverItemResponse response = new HandoverItemResponse
        {
            Id = item.Id,
            Name = item.Name,
            Kind = item.Kind.ToApiString(),
            FileName = item.FileName,
            ContentType = item.ContentType,
            SizeBytes = item.SizeBytes
        };
        return response;
    }
}
