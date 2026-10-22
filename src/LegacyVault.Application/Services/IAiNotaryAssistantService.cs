using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Trợ lý Trí tuệ Nhân tạo Gemini AI Co-Pilot hỗ trợ Công chứng viên (Verifier) thẩm định tính hợp pháp của hồ sơ báo tử.
/// </summary>
public interface IAiNotaryAssistantService
{
    Task<AiDossierAnalysisResponse> AssessDossierAsync(Guid caseId, CancellationToken ct);
}
