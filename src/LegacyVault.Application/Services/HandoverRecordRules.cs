using System.Security.Cryptography;
using System.Text;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật của biên bản bàn giao, không đọc ghi dữ liệu. Thứ tự ba bước: biên bản → danh tính người nhận → chữ ký ba bên.
/// Bước trước chưa xong thì chưa mở bước sau.
/// </summary>
public static class HandoverRecordRules
{
    /// <summary>Số ký tự của Id biên bản dùng làm mã rút gọn trong thư.</summary>
    public const int CaseCodeLength = 8;

    /// <summary>
    /// Mã hồ sơ rút gọn trong thư: 8 ký tự hex CUỐI của Id biên bản, chữ hoa. Không lấy phần đầu vì Guid v7 bắt đầu bằng
    /// mốc thời gian, các biên bản tạo gần nhau sẽ trùng mã.
    /// </summary>
    public static string CaseCode(Guid recordId)
    {
        string hex = recordId.ToString("N");
        return hex.Substring(hex.Length - CaseCodeLength).ToUpperInvariant();
    }

    /// <summary>Gom tài sản theo người nhận, giữ thứ tự dòng đã tạo.</summary>
    public static Dictionary<Guid, List<Guid>> GroupAssetsByRecipient(IEnumerable<HandoverRecordLine> lines)
    {
        Dictionary<Guid, List<Guid>> result = new Dictionary<Guid, List<Guid>>();
        List<HandoverRecordLine> ordered = lines.OrderBy(l => l.CreatedAt).ThenBy(l => l.Id).ToList();
        foreach (HandoverRecordLine line in ordered)
        {
            if (!result.ContainsKey(line.BeneficiaryId))
            {
                result[line.BeneficiaryId] = new List<Guid>();
            }
            result[line.BeneficiaryId].Add(line.AssetId);
        }
        return result;
    }

    /// <summary>Chữ ký của bản hiện tại (bản cũ đã mất hiệu lực khi luật sư mở lại biên bản).</summary>
    public static List<HandoverRecordSignature> CurrentSignatures(HandoverRecord record)
    {
        List<HandoverRecordSignature> result = new List<HandoverRecordSignature>();
        foreach (HandoverRecordSignature signature in record.Signatures)
        {
            if (signature.RevisionNo == record.RevisionNo)
            {
                result.Add(signature);
            }
        }
        return result;
    }

    /// <summary>Chữ ký hiện tại của một bên; với người nhận thì truyền beneficiaryId. Null khi chưa ký.</summary>
    public static HandoverRecordSignature? FindSignature(HandoverRecord record, HandoverSignerRole role, Guid? beneficiaryId)
    {
        foreach (HandoverRecordSignature signature in CurrentSignatures(record))
        {
            if (signature.Role != role)
            {
                continue;
            }
            if (role == HandoverSignerRole.Recipient && signature.BeneficiaryId != beneficiaryId)
            {
                continue;
            }
            return signature;
        }
        return null;
    }

    /// <summary>Danh tính đã được luật sư xác nhận: lượt đã qua bước xác minh (hoặc đã bàn giao).</summary>
    public static bool IsIdentityConfirmed(HandoverTurn turn)
    {
        return turn.Status == HandoverTurnStatus.ReadyToAccept
            || turn.Status == HandoverTurnStatus.HandoverCommitted;
    }

    /// <summary>Mọi người nhận trong biên bản đều đã có lượt và đã được xác nhận danh tính.</summary>
    public static bool AllIdentitiesConfirmed(HandoverRecord record, Dictionary<Guid, HandoverTurn> turnByRecipient)
    {
        Dictionary<Guid, List<Guid>> recipients = GroupAssetsByRecipient(record.Lines);
        if (recipients.Count == 0)
        {
            return false;
        }
        foreach (Guid beneficiaryId in recipients.Keys)
        {
            if (!turnByRecipient.ContainsKey(beneficiaryId))
            {
                return false;
            }
            if (!IsIdentityConfirmed(turnByRecipient[beneficiaryId]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Đủ chữ ký ba bên của bản hiện tại: luật sư, Người thực thi và từng người nhận trong biên bản.</summary>
    public static bool AllSigned(HandoverRecord record)
    {
        if (FindSignature(record, HandoverSignerRole.Verifier, null) == null)
        {
            return false;
        }
        if (FindSignature(record, HandoverSignerRole.Executor, null) == null)
        {
            return false;
        }
        Dictionary<Guid, List<Guid>> recipients = GroupAssetsByRecipient(record.Lines);
        foreach (Guid beneficiaryId in recipients.Keys)
        {
            if (FindSignature(record, HandoverSignerRole.Recipient, beneficiaryId) == null)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Kiểm danh sách dòng luật sư gửi lên: không trống, không trùng cặp người nhận - tài sản, người nhận và tài sản phải thuộc kho.
    /// Trả câu lỗi đầu tiên gặp, hoặc null khi hợp lệ.
    /// </summary>
    public static string? ValidateLines(
        List<(Guid BeneficiaryId, Guid AssetId)> lines, HashSet<Guid> vaultBeneficiaryIds, HashSet<Guid> vaultAssetIds)
    {
        if (lines.Count == 0)
        {
            return "Biên bản phải có ít nhất một người nhận một tài sản.";
        }
        HashSet<(Guid, Guid)> seenPairs = new HashSet<(Guid, Guid)>();
        foreach ((Guid beneficiaryId, Guid assetId) in lines)
        {
            if (!vaultBeneficiaryIds.Contains(beneficiaryId))
            {
                return "Có người nhận không thuộc kho này.";
            }
            if (!vaultAssetIds.Contains(assetId))
            {
                return "Có tài sản không thuộc kho này hoặc đã bị xóa.";
            }
            if (seenPairs.Contains((beneficiaryId, assetId)))
            {
                return "Có dòng bị trùng: một người nhận chỉ cần ghi một lần cho mỗi tài sản.";
            }
            seenPairs.Add((beneficiaryId, assetId));
        }
        return null;
    }

    /// <summary>
    /// Trước khi chốt: mọi yêu cầu nhận phải đã xét (không còn chờ xét hay chờ bổ sung) và có ít nhất một yêu cầu được
    /// chấp nhận. Trả câu lỗi, hoặc null khi hợp lệ.
    /// </summary>
    public static string? ValidateClaimsForFinalize(List<HandoverClaim> claims)
    {
        bool anyAccepted = false;
        foreach (HandoverClaim claim in claims)
        {
            if (claim.Status == HandoverClaimStatus.Submitted || claim.Status == HandoverClaimStatus.SupplementRequested)
            {
                return "Còn yêu cầu nhận tài sản chưa xét xong. Xét giấy tờ của mọi người nhận trước khi chốt.";
            }
            if (claim.Status == HandoverClaimStatus.Accepted)
            {
                anyAccepted = true;
            }
        }
        if (!anyAccepted)
        {
            return "Chưa có yêu cầu nhận tài sản nào được chấp nhận.";
        }
        return null;
    }

    /// <summary>Link riêng gửi yêu cầu nhận tài sản: chuỗi ngẫu nhiên 32 byte (base64url) và băm SHA-256 để lưu DB.</summary>
    public static (string Value, string Hash) CreateRequestToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        string value = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (value, HashRequestToken(value));
    }

    public static string HashRequestToken(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }
}
