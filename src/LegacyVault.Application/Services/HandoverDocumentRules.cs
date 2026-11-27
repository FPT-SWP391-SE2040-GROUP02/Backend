using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật giấy tờ khi nộp lần đầu, dùng chung cho yêu cầu nhận tài sản và hồ sơ báo tin: bắt buộc giấy chứng tử và di
/// chúc, giấy tờ khác không bắt buộc; tổng 1–10 tệp, mỗi tệp tối đa 10 MiB, PDF hoặc ảnh. Nộp bổ sung không dùng luật này.
/// </summary>
public static class HandoverDocumentRules
{
    /// <summary>Thiếu loại bắt buộc thì 400 đúng trường (deathCertificates, wills); sai số tệp, dung lượng, loại tệp thì trường files.</summary>
    public static void ValidateFirstSubmission(HandoverDocumentUploads uploads)
    {
        if (uploads.DeathCertificates.Count == 0)
        {
            throw ValidationErrors.For("deathCertificates", "Vui lòng đính kèm giấy chứng tử.");
        }
        if (uploads.Wills.Count == 0)
        {
            throw ValidationErrors.For("wills", "Vui lòng đính kèm di chúc.");
        }
        List<UploadedFile> all = new List<UploadedFile>();
        all.AddRange(uploads.DeathCertificates);
        all.AddRange(uploads.Wills);
        all.AddRange(uploads.Others);
        HandoverRecordFileStorage.Validate(all, true);
    }
}
