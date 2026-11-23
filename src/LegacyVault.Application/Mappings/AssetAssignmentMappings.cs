using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class AssetAssignmentMappings
{
    public static AssetAssignmentResponse ToResponse(this AssetAssignment assignment)
    {
        return new AssetAssignmentResponse
        {
            Id = assignment.Id,
            AssetId = assignment.AssetId,
            BeneficiaryId = assignment.BeneficiaryId,
            CreatedAt = assignment.CreatedAt,
        };
    }

    public static List<AssetAssignmentResponse> ToResponses(this List<AssetAssignment> assignments)
    {
        List<AssetAssignmentResponse> responses = new List<AssetAssignmentResponse>();
        foreach (AssetAssignment assignment in assignments)
        {
            responses.Add(assignment.ToResponse());
        }
        return responses;
    }
}
