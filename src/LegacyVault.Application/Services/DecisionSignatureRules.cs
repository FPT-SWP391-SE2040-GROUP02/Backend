using System.Security.Cryptography;
using System.Text;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// DEATH-14: luật thuần dựng chuỗi được ký cho quyết định cuối của Verifier. Hàm thuần, không đọc DB.
/// </summary>
/// <remarks>
/// Lúc ký và lúc kiểm đều gọi BuildPayload; chuỗi không lưu vào DB mà dựng lại từ dữ liệu đã lưu,
/// nên sửa bất kỳ cột nào nằm trong chuỗi thì chữ ký không còn khớp.
/// </remarks>
public static class DecisionSignatureRules
{
    /// <summary>DEATH-14: RSA-2048, băm SHA-256, đệm PSS.</summary>
    public const string Algorithm = "RSA-2048-PSS-SHA256";

    /// <summary>Tiền tố cố định để chuỗi ký quyết định chứng tử không lẫn với loại dữ liệu khác.</summary>
    public const string PayloadPrefix = "LV-DEATH-DECISION";

    /// <summary>Phiên bản định dạng chuỗi; đổi cách dựng chuỗi thì tăng số này.</summary>
    public const string PayloadVersion = "v1";

    /// <summary>DEATH-14, D12: chữ ký khớp dữ liệu hiện tại và khóa đúng cặp.</summary>
    public const string StatusValid = "VALID";

    /// <summary>DEATH-14, D12: dữ liệu đã bị sửa sau khi ký, hoặc khóa/chữ ký không khớp.</summary>
    public const string StatusInvalid = "INVALID";

    /// <summary>DEATH-14, D12: quyết định cuối không có chữ ký (quyết định cũ trước khi có chữ ký số).</summary>
    public const string StatusNotSigned = "NOT_SIGNED";

    /// <summary>DEATH-14, D12: hồ sơ chưa có quyết định duyệt hay từ chối.</summary>
    public const string StatusNoFinalDecision = "NO_FINAL_DECISION";

    private const char Separator = '|';

    private const int FingerprintHexLength = 16;

    private const int FingerprintGroupLength = 4;

    /// <summary>DEATH-14: chỉ ký quyết định cuối (duyệt, từ chối); yêu cầu bổ sung không ký.</summary>
    public static bool IsSignedKind(DeathCaseDecisionKind kind)
    {
        if (kind == DeathCaseDecisionKind.Approved || kind == DeathCaseDecisionKind.Rejected)
        {
            return true;
        }
        return false;
    }

    /// <summary>Tên loại quyết định trong chuỗi ký và trong phiếu chữ ký.</summary>
    public static string KindText(DeathCaseDecisionKind kind)
    {
        if (kind == DeathCaseDecisionKind.Approved)
        {
            return "APPROVED";
        }
        if (kind == DeathCaseDecisionKind.Rejected)
        {
            return "REJECTED";
        }
        throw new ArgumentException("Chỉ quyết định duyệt hoặc từ chối mới được ký số.", nameof(kind));
    }

    /// <summary>Id các tệp của một bản hồ sơ, sắp theo Position (thứ tự Executor tải lên).</summary>
    public static List<Guid> FileIdsInPosition(DeathCaseVersion version)
    {
        List<DeathCaseFile> files = new List<DeathCaseFile>(version.Files);
        files.Sort(CompareByPosition);

        List<Guid> fileIds = new List<Guid>();
        foreach (DeathCaseFile file in files)
        {
            fileIds.Add(file.Id);
        }
        return fileIds;
    }

    /// <summary>
    /// DEATH-14: dựng chuỗi được ký (UTF-8):
    /// LV-DEATH-DECISION|v1|{decisionId}|{caseId}|{versionNo}|{KIND}|{verifierUserId}|{decidedAtUtcTicks}|{reasonBase64}|{fileIds}.
    /// </summary>
    /// <remarks>
    /// - Thời điểm dùng UtcTicks (số nguyên) để đọc lại từ DB không lệch định dạng ngày giờ.
    /// - Lý do mã Base64 để ký tự '|' trong lý do không phá cấu trúc chuỗi.
    /// - fileIdsInOrder phải đã sắp theo Position (dùng FileIdsInPosition); rỗng nếu bản không có tệp.
    /// </remarks>
    public static byte[] BuildPayload(DeathCaseDecision decision, IReadOnlyList<Guid> fileIdsInOrder)
    {
        string kindText = KindText(decision.Kind);
        string reasonBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(decision.Reason));

        StringBuilder fileIdsText = new StringBuilder();
        foreach (Guid fileId in fileIdsInOrder)
        {
            if (fileIdsText.Length > 0)
            {
                fileIdsText.Append(',');
            }
            fileIdsText.Append(fileId.ToString("N"));
        }

        StringBuilder payload = new StringBuilder();
        payload.Append(PayloadPrefix);
        payload.Append(Separator).Append(PayloadVersion);
        payload.Append(Separator).Append(decision.Id.ToString("N"));
        payload.Append(Separator).Append(decision.CaseId.ToString("N"));
        payload.Append(Separator).Append(decision.VersionNo.ToString(System.Globalization.CultureInfo.InvariantCulture));
        payload.Append(Separator).Append(kindText);
        payload.Append(Separator).Append(decision.VerifierUserId.ToString("N"));
        payload.Append(Separator).Append(decision.DecidedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        payload.Append(Separator).Append(reasonBase64);
        payload.Append(Separator).Append(fileIdsText.ToString());
        return Encoding.UTF8.GetBytes(payload.ToString());
    }

    /// <summary>Dấu vân tay khóa công khai để người xem đối chiếu: 16 ký tự hex đầu của SHA-256, nhóm 4 nối bằng '-'.</summary>
    public static string Fingerprint(byte[] publicKey)
    {
        byte[] hash = SHA256.HashData(publicKey);
        string hex = Convert.ToHexString(hash).Substring(0, FingerprintHexLength);

        StringBuilder fingerprint = new StringBuilder();
        for (int index = 0; index < hex.Length; index += FingerprintGroupLength)
        {
            if (fingerprint.Length > 0)
            {
                fingerprint.Append('-');
            }
            fingerprint.Append(hex, index, FingerprintGroupLength);
        }
        return fingerprint.ToString();
    }

    private static int CompareByPosition(DeathCaseFile left, DeathCaseFile right)
    {
        return left.Position.CompareTo(right.Position);
    }
}
