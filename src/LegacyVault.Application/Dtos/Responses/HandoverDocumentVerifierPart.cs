namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Phần của Người xét duyệt: đã kiểm tra thủ công và chịu trách nhiệm.</summary>
public class HandoverDocumentVerifierPart
{
    public string SignedByName { get; set; } = string.Empty;

    /// <summary>Các mục kiểm tra đã tick (biên bản mới có 2 mục, biên bản cũ có thể có 4), rỗng khi chưa kiểm tra.</summary>
    public List<bool> Checks { get; set; } = new List<bool>();

    public DateTimeOffset? SignedAt { get; set; }
}
