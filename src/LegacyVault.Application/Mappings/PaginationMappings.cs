using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Mappings;

public static class PaginationMappings
{
    public static PaginatedList<T> ToPaginatedList<T>(this List<T> items, int totalCount, PaginationRequest request)
    {
        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);
        return new PaginatedList<T>
        {
            Items = items,
            TotalCount = totalCount,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalPages = totalPages,
            HasPreviousPage = request.PageIndex > 1,
            HasNextPage = request.PageIndex < totalPages,
        };
    }
}
