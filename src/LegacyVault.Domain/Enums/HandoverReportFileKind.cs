namespace LegacyVault.Domain.Enums;

/// <summary>Loại giấy tờ trong hồ sơ báo tin. Giá trị lưu trong DB là tên enum; không đổi tên.</summary>
public enum HandoverReportFileKind
{
    /// <summary>Giấy chứng tử của Chủ kho.</summary>
    DeathCertificate = 0,

    /// <summary>Di chúc hoặc văn bản phân chia (không bắt buộc).</summary>
    Will = 1,

    /// <summary>Giấy tờ khác, gồm cả giấy tờ nộp bổ sung.</summary>
    Other = 2,
}
