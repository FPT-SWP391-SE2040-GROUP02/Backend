namespace LegacyVault.Domain.Enums;

/// <summary>Tệp đính kèm biên bản do ai nộp. Giá trị lưu trong DB là tên enum; không đổi tên.</summary>
public enum HandoverRecordFileKind
{
    /// <summary>
    /// Giấy tờ chưa phân loại của yêu cầu nhận tài sản: tệp nộp bổ sung, và tệp của yêu cầu lưu trước khi chia loại.
    /// </summary>
    ClaimDocument = 0,

    /// <summary>Tệp căn cứ luật sư đính kèm vào biên bản.</summary>
    Evidence = 1,

    /// <summary>Di chúc người nhận nộp kèm yêu cầu nhận tài sản (bắt buộc).</summary>
    Will = 2,

    /// <summary>Giấy chứng tử người nhận nộp kèm yêu cầu nhận tài sản (bắt buộc).</summary>
    DeathCertificate = 3,

    /// <summary>Giấy tờ khác người nhận nộp kèm yêu cầu nhận tài sản.</summary>
    Other = 4,
}
