using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Hằng số và luật thuần của biên bản đồng ý nhận hoặc văn bản từ chối. Không có hạn chót nào.</summary>
public static class HandoverDocumentRules
{
    /// <summary>Phiên bản mẫu chữ của biên bản; đổi khi sửa chữ trong văn bản.</summary>
    public const string TemplateVersion = "bb-2026-11-b";

    /// <summary>Khóa tham số Admin sửa được: câu báo trong biên bản đồng ý về việc xử lý phần tài sản khi có người nhận từ chối.</summary>
    public const string DeclineNoticeSettingKey = "Handover.DeclineNotice";

    /// <summary>Mức gốc của câu báo khi Admin chưa đặt hoặc để trống.</summary>
    public const string DefaultDeclineNotice =
        "Nếu một người nhận từ chối nhận, phần tài sản của gói bàn giao đó sẽ bị xóa hoàn toàn khỏi hệ thống và không thể khôi phục.";

    /// <summary>
    /// Câu báo trong biên bản đồng ý: cách xử lý phần tài sản khi có người nhận từ chối. Admin sửa ở Cấu hình, thiếu hoặc
    /// để trống thì dùng <see cref="DefaultDeclineNotice"/>.
    /// </summary>
    public static string DeclineNotice
    {
        get
        {
            return RuntimeSettings.Text(DeclineNoticeSettingKey, DefaultDeclineNotice);
        }
    }

    /// <summary>Phiên bản điều khoản mà người ký tick đồng ý; đổi khi sửa chữ điều khoản.</summary>
    public const string TermsVersion = "terms-2026-11";

    /// <summary>Số mục Người xét duyệt phải kiểm tra thủ công và tick đủ.</summary>
    public const int VerifierCheckCount = 4;

    /// <summary>Độ dài tối đa của nơi thường trú lưu trong biên bản (cột OwnerResidenceAddress, RecipientResidenceAddress).</summary>
    public const int ResidenceAddressMaxLength = 500;

    /// <summary>Nơi thường trú in vào biên bản: gộp khoảng trắng và xuống dòng thành một dấu cách, cắt theo độ dài cột.</summary>
    public static string CleanAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }
        string joined = string.Join(' ', address.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return joined.Length <= ResidenceAddressMaxLength ? joined : joined.Substring(0, ResidenceAddressMaxLength);
    }

    /// <summary>Mã ngắn của biên bản: 8 ký tự cuối id, in hoa.</summary>
    public static string Code(Guid documentId)
    {
        return documentId.ToString("N").Substring(24).ToUpperInvariant();
    }

    /// <summary>Chuỗi lưu bốn mục kiểm tra, ví dụ "1111".</summary>
    public static string EncodeChecks(List<bool> checks)
    {
        string text = string.Empty;
        foreach (bool check in checks)
        {
            text += check ? "1" : "0";
        }
        return text;
    }

    public static List<bool> DecodeChecks(string text)
    {
        List<bool> checks = new List<bool>();
        foreach (char c in text)
        {
            checks.Add(c == '1');
        }
        return checks;
    }

    /// <summary>true khi danh sách có đúng VerifierCheckCount mục và tất cả đều tick.</summary>
    public static bool AllChecked(List<bool> checks)
    {
        if (checks.Count != VerifierCheckCount)
        {
            return false;
        }
        foreach (bool check in checks)
        {
            if (!check)
            {
                return false;
            }
        }
        return true;
    }

    public static HandoverSubmissionKind ParseKind(string kind)
    {
        return kind == "DECLINE" ? HandoverSubmissionKind.Decline : HandoverSubmissionKind.Accept;
    }

    public static string StatusToApi(HandoverDocumentStatus status)
    {
        switch (status)
        {
            case HandoverDocumentStatus.WaitRecipient:
                return "WAIT_RECIPIENT";
            case HandoverDocumentStatus.WaitVerifier:
                return "WAIT_VERIFIER";
            default:
                return "COMPLETED";
        }
    }

    /// <summary>Người nhận chưa nộp hồ sơ lần nào thì còn đổi được lựa chọn Chấp nhận hoặc Từ chối; đã nộp rồi (kể cả bị trả về sửa) thì không đổi nữa.</summary>
    public static bool CanChangeKind(HandoverDocument document)
    {
        if (document.Status != HandoverDocumentStatus.WaitRecipient)
        {
            return false;
        }
        return document.SubmissionId == null;
    }
}
