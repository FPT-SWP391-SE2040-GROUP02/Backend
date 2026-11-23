using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Bước hiện tại của MỘT người nhận ở một kho (màn "Nhận tài sản" của người nhận), suy ra từ dữ liệu có sẵn:
/// biên bản đang mở của kho (HandoverRecord), yêu cầu nhận của người này (HandoverClaim), dòng biên bản, lượt bàn giao
/// (HandoverTurn), chữ ký và quyền tải (HandoverGrant). Không đọc ghi dữ liệu.
/// </summary>
/// <remarks>
/// Bảng ánh xạ (xét từ trên xuống, gặp dòng đúng đầu tiên thì dừng):
/// | Điều kiện                                                                  | Trạng thái           | needsAction |
/// |----------------------------------------------------------------------------|----------------------|-------------|
/// | Kho chưa có biên bản đang mở (chưa ai gửi yêu cầu, hoặc Chủ kho đã hủy)       | NOT_REQUESTED        | false       |
/// | Có yêu cầu của tôi, trạng thái Submitted                                     | REQUEST_PENDING      | false       |
/// | Có yêu cầu của tôi, trạng thái SupplementRequested                           | SUPPLEMENT_REQUESTED | true        |
/// | Có yêu cầu của tôi, trạng thái Rejected                                      | REJECTED             | false       |
/// | Không có yêu cầu của tôi, không có tên trong biên bản, không có lượt          | NOT_REQUESTED        | true khi biên bản chưa hoàn tất (người khác đã gửi, tôi gửi được), false khi đã hoàn tất |
/// | (Yêu cầu đã chấp nhận, hoặc luật sư tự thêm tôi vào biên bản) biên bản đang lập hoặc chờ danh tính | IN_REVIEW | false |
/// | Biên bản ở bước ký, không có tên tôi trong biên bản                           | IN_REVIEW            | false       |
/// | Biên bản ở bước ký, tôi chưa ký bản hiện tại                                  | NEED_MY_SIGNATURE    | true        |
/// | Biên bản ở bước ký, tôi đã ký bản hiện tại                                    | WAITING_OTHERS       | false       |
/// | Biên bản đã hoàn tất, không có tên tôi trong biên bản                         | REJECTED             | false       |
/// | Biên bản đã hoàn tất, lượt của tôi đã bấm "Tôi đã nhận đủ" (ReceivedAllAt)    | RECEIVED             | false       |
/// | Biên bản đã hoàn tất, lượt của tôi đã có quyền tải (Grant)                    | CODE_READY           | true        |
/// | Biên bản đã hoàn tất, lượt chưa có quyền tải (trường hợp lạ)                  | WAITING_OTHERS       | false       |
/// Ở bước danh tính (IdentityCheck) người nhận vẫn nộp ảnh căn cước ở cổng người nhận; bảng xếp vào IN_REVIEW vì đó là
/// bước luật sư đang kiểm.
/// </remarks>
public static class RecipientStepRules
{
    public const string NotRequested = "NOT_REQUESTED";
    public const string RequestPending = "REQUEST_PENDING";
    public const string SupplementRequested = "SUPPLEMENT_REQUESTED";
    public const string Rejected = "REJECTED";
    public const string InReview = "IN_REVIEW";
    public const string NeedMySignature = "NEED_MY_SIGNATURE";
    public const string WaitingOthers = "WAITING_OTHERS";
    public const string CodeReady = "CODE_READY";
    public const string Received = "RECEIVED";

    /// <summary>
    /// Trạng thái bước của người nhận. record là biên bản đang mở của kho (null khi chưa có), turn là lượt đang hiệu lực
    /// của người nhận trong biên bản đó (null khi chưa có).
    /// </summary>
    public static string ResolveStatus(HandoverRecord? record, Guid beneficiaryId, HandoverTurn? turn)
    {
        if (record == null)
        {
            return NotRequested;
        }

        // 1. Kết quả xét giấy tờ của chính yêu cầu tôi gửi
        HandoverClaim? claim = FindClaim(record, beneficiaryId);
        if (claim != null)
        {
            if (claim.Status == HandoverClaimStatus.Submitted)
            {
                return RequestPending;
            }
            if (claim.Status == HandoverClaimStatus.SupplementRequested)
            {
                return SupplementRequested;
            }
            if (claim.Status == HandoverClaimStatus.Rejected)
            {
                return Rejected;
            }
        }

        // 2. Chưa gửi yêu cầu và cũng không được luật sư ghi vào biên bản
        bool inRecord = HasLines(record, beneficiaryId);
        if (claim == null && !inRecord && turn == null)
        {
            return NotRequested;
        }

        // 3. Đã được nhận vào biên bản: theo bước của biên bản
        if (record.Status == HandoverRecordStatus.Signing)
        {
            if (!inRecord)
            {
                return InReview;
            }
            HandoverRecordSignature? mySignature =
                HandoverRecordRules.FindSignature(record, HandoverSignerRole.Recipient, beneficiaryId);
            if (mySignature == null)
            {
                return NeedMySignature;
            }
            return WaitingOthers;
        }
        if (record.Status == HandoverRecordStatus.Completed)
        {
            if (!inRecord)
            {
                return Rejected;
            }
            if (turn != null && turn.ReceivedAllAt != null)
            {
                return Received;
            }
            if (turn != null && turn.Grant != null)
            {
                return CodeReady;
            }
            return WaitingOthers;
        }
        return InReview;
    }

    /// <summary>
    /// Có cần người nhận làm gì không. NOT_REQUESTED chỉ cần khi kho đã có biên bản đang mở chưa hoàn tất (người khác đã
    /// gửi yêu cầu); SUPPLEMENT_REQUESTED, NEED_MY_SIGNATURE, CODE_READY luôn cần.
    /// </summary>
    public static bool NeedsAction(string status, HandoverRecord? record)
    {
        if (status == SupplementRequested || status == NeedMySignature || status == CodeReady)
        {
            return true;
        }
        if (status == NotRequested)
        {
            return record != null && record.Status != HandoverRecordStatus.Completed;
        }
        return false;
    }

    /// <summary>
    /// Người nhận gửi được yêu cầu lúc này không (cùng điều kiện với luồng gửi bằng link): kho chưa có biên bản thì kế hoạch
    /// phải đang hoạt động; có biên bản thì biên bản chưa hoàn tất. Chỉ áp dụng khi trạng thái là NOT_REQUESTED.
    /// </summary>
    public static bool CanRequest(string status, PlanStatus planStatus, HandoverRecord? record)
    {
        if (status != NotRequested)
        {
            return false;
        }
        if (record == null)
        {
            return planStatus == PlanStatus.Active;
        }
        return record.Status != HandoverRecordStatus.Completed;
    }

    private static HandoverClaim? FindClaim(HandoverRecord record, Guid beneficiaryId)
    {
        foreach (HandoverClaim claim in record.Claims)
        {
            if (claim.BeneficiaryId == beneficiaryId)
            {
                return claim;
            }
        }
        return null;
    }

    private static bool HasLines(HandoverRecord record, Guid beneficiaryId)
    {
        foreach (HandoverRecordLine line in record.Lines)
        {
            if (line.BeneficiaryId == beneficiaryId)
            {
                return true;
            }
        }
        return false;
    }
}
