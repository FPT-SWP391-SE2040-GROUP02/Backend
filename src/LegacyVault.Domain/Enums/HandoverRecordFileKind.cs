namespace LegacyVault.Domain.Enums;

/// <summary>Tệp đính kèm biên bản do ai nộp. Giá trị lưu trong DB là tên enum; không đổi tên.</summary>
public enum HandoverRecordFileKind
{
    /// <summary>Giấy tờ người nhận nộp kèm yêu cầu nhận tài sản (di chúc, văn bản phân chia, bản án...).</summary>
    ClaimDocument = 0,

    /// <summary>Tệp căn cứ luật sư đính kèm vào biên bản.</summary>
    Evidence = 1,

    /// <summary>Di chúc người nhận nộp kèm yêu cầu nhận tài sản (bắt buộc).</summary>
    Will = 2,
}
