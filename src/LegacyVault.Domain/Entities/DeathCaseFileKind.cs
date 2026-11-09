namespace LegacyVault.Domain.Entities;

/// <summary>
/// Loại giấy của một tệp trong hồ sơ chứng tử (cột DeathCaseFiles.Kind). Mỗi loại tối đa một tệp khi FE gửi kind rõ ràng.
/// </summary>
public static class DeathCaseFileKind
{
    /// <summary>Giấy chứng tử hoặc trích lục khai tử. Bắt buộc để gửi hồ sơ.</summary>
    public const string DeathCertificate = "DEATH_CERTIFICATE";

    /// <summary>Giấy báo tử hoặc giấy tờ thay thế (quyết định tuyên bố chết, văn bản của công an...). Không bắt buộc.</summary>
    public const string DeathNotice = "DEATH_NOTICE";

    public static bool IsValid(string? kind)
    {
        return kind == DeathCertificate || kind == DeathNotice;
    }
}
