namespace LegacyVault.Application.Services;

/// <summary>
/// ETag chống ghi đè (docs/api-contract.md mục 4), tạo từ UpdatedAt của entity: chưa sửa lần nào → "\"0\"".
/// </summary>
/// <remarks>
/// So sánh ETag ở service chỉ chặn sớm; chặn thật nằm ở DB (UpdatedAt là concurrency token,
/// UPDATE ... WHERE UpdatedAt = giá trị cũ) để hai request đồng thời không ghi đè nhau.
/// </remarks>
public static class Etag
{
    public static string From(DateTimeOffset? updatedAt)
    {
        return $"\"{updatedAt?.UtcTicks ?? 0}\"";
    }

    public static bool Matches(string? etag, DateTimeOffset? updatedAt)
    {
        return string.Equals(etag?.Trim(), From(updatedAt), StringComparison.Ordinal);
    }
}
