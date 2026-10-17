using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// Chọn Verifier cho hồ sơ chứng tử ở MỘT chỗ duy nhất (D5), dùng cho cả lúc Executor gửi hồ sơ
/// và job phân công.
/// </summary>
public interface IDeathCaseVerifierPicker
{
    /// <summary>
    /// ASSIGN-01, ROLE-03: chọn Verifier xoay vòng, loại Owner, Executor đang hiệu lực của kho, người gửi bản
    /// đang xét và (khi giao lại) Verifier đang giữ hồ sơ. Không ai phù hợp → null.
    /// </summary>
    Task<Guid?> PickAsync(Vault vault, Guid submitterUserId, Guid? currentVerifierUserId, CancellationToken ct);
}
