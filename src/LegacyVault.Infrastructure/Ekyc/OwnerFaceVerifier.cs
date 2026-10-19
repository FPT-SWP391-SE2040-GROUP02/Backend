using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Infrastructure.Ekyc;

/// <summary>
/// So selfie của Owner với mẫu khuôn mặt đã lưu (ALIVE-02, EKYC-26): lấy vector của selfie từ dịch vụ eKYC,
/// giải mã mẫu rồi tự tính cosine với ngưỡng của server.
/// </summary>
/// <remarks>
/// - Chưa có mẫu, hoặc dịch vụ eKYC không dùng được: Unavailable, không tính là một lần thất bại (EKYC-19).
/// - Ảnh không có đúng một khuôn mặt: để lỗi 400 EKYC_IMAGE_REJECTED đi tiếp, người dùng chụp lại và không bị tính thất bại.
/// - Mẫu tạo bởi model khác với model đang chạy thì vector không so được, coi như Unavailable.
/// - Không log vector, ảnh hay điểm số.
/// </remarks>
public sealed class OwnerFaceVerifier : IOwnerFaceVerifier
{
    private readonly IOwnerFaceTemplateRepository templateRepository;
    private readonly IFaceTemplateProtector templateProtector;
    private readonly IEkycClient ekycClient;

    public OwnerFaceVerifier(
        IOwnerFaceTemplateRepository templateRepository,
        IFaceTemplateProtector templateProtector,
        IEkycClient ekycClient)
    {
        this.templateRepository = templateRepository;
        this.templateProtector = templateProtector;
        this.ekycClient = ekycClient;
    }

    public async Task<OwnerFaceOutcome> VerifyAsync(
        Guid ownerUserId, byte[] selfie, string fileName, string contentType, CancellationToken ct)
    {
        OwnerFaceTemplate? template = await templateRepository.FindLatestByUserIdAsync(ownerUserId, ct);
        if (template == null)
        {
            return OwnerFaceOutcome.Unavailable;
        }

        EkycFaceEmbedding probe;
        try
        {
            probe = await ekycClient.GetFaceEmbeddingAsync(selfie, fileName, contentType, ct);
        }
        catch (AppException ex) when (ex.Code == ErrorCode.EkycServiceUnavailable)
        {
            return OwnerFaceOutcome.Unavailable;
        }

        if (!string.Equals(probe.ModelName, template.ModelName, StringComparison.Ordinal)
            || probe.Values.Length != template.Dimension)
        {
            return OwnerFaceOutcome.Unavailable;
        }

        float[] stored = templateProtector.Unprotect(template);
        double score = EkycRules.CosineSimilarity(stored, probe.Values);
        if (score >= EkycRules.MatchThreshold)
        {
            return OwnerFaceOutcome.Matched;
        }
        return OwnerFaceOutcome.NotMatched;
    }
}
