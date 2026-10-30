using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

/// <summary>DEATH-14: dựng phiếu chữ ký số từ kết quả kiểm. Không đưa khóa riêng, DEK hay mật khẩu ra ngoài.</summary>
public static class DecisionSignatureMappings
{
    /// <param name="status">Kết quả kiểm (DecisionSignatureRules.Status*).</param>
    /// <param name="decision">Quyết định cuối; null khi chưa có.</param>
    /// <param name="key">Khóa đã ký; null khi chưa ký hoặc không tìm thấy khóa.</param>
    /// <param name="signedByFullName">Họ tên Verifier đã quyết định; null khi không còn tài khoản.</param>
    public static DecisionSignatureResponse ToSignatureResponse(
        string status, DeathCaseDecision? decision, VerifierSigningKey? key, string? signedByFullName)
    {
        DecisionSignatureResponse response = new DecisionSignatureResponse();
        response.Status = status;
        if (decision == null)
        {
            return response;
        }

        response.DecisionKind = DecisionSignatureRules.KindText(decision.Kind);
        response.VersionNo = decision.VersionNo;
        response.DecidedAt = decision.DecidedAt;
        response.SignedByUserId = decision.VerifierUserId;
        response.SignedByFullName = signedByFullName;
        if (decision.Signature != null)
        {
            response.SignatureBase64 = Convert.ToBase64String(decision.Signature);
        }
        if (key != null)
        {
            response.Algorithm = key.Algorithm;
            response.KeyFingerprint = DecisionSignatureRules.Fingerprint(key.PublicKey);
        }
        return response;
    }
}
