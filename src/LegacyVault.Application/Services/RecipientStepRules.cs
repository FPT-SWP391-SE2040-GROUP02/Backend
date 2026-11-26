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
/// | Kho chưa có biên bản đang mở (chưa ai gửi yêu cầu, hoặc Chủ tài sản đã hủy)       | NOT_REQUESTED        | false       |
/// | Có yêu cầu của tôi, trạng thái Submitted                                     | REQUEST_PENDING      | false       |
/// | Có yêu cầu của tôi, trạng thái SupplementRequested                           | SUPPLEMENT_REQUESTED | true        |
/// | Có yêu cầu của tôi, trạng thái Rejected                                      | REJECTED             | false       |
/// | Không có yêu cầu của tôi, không có tên trong biên bản, không có lượt          | NOT_REQUESTED        | true khi biên bản chưa hoàn tất (người khác đã gửi, tôi gửi được), false khi đã hoàn tất |
/// | Biên bản đang lập (yêu cầu đã chấp nhận, hoặc luật sư tự thêm tôi)            | IN_REVIEW            | false       |
/// | Biên bản đã hoàn tất, không có tên tôi                                       | REJECTED             | false       |
/// | Biên bản đã chốt, không có tên tôi, hoặc tôi chưa có lượt                     | IN_REVIEW            | false       |
/// | Lượt của tôi đã bấm "Tôi đã nhận đủ" (ReceivedAllAt)                          | RECEIVED             | false       |
/// | Lượt của tôi đã có quyền tải (Grant), dù người nhận khác chưa ký              | CODE_READY           | true        |
/// | Lượt chưa xác nhận danh tính, chưa nộp ảnh căn cước hoặc bị yêu cầu bổ sung   | NEED_IDENTITY        | true        |
/// | Lượt chưa xác nhận danh tính, đã nộp ảnh căn cước, đang chờ luật sư duyệt     | IN_REVIEW            | false       |
/// | Danh tính của tôi đã xong, tôi ký được ngay (Người thực thi đã ký phần của tôi) | NEED_MY_SIGNATURE    | true        |
/// | Danh tính của tôi đã xong, còn chờ luật sư hoặc Người thực thi ký phần của tôi | WAITING_OTHERS       | false       |
/// | Tôi đã ký, chưa được mở quyền tải                                             | WAITING_OTHERS       | false       |
/// Mỗi người nhận đi riêng theo lượt của chính mình: không chờ người nhận khác nộp ảnh hay ký.
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
    public const string NeedIdentity = "NEED_IDENTITY";

    /// <summary>
    /// Trạng thái bước của người nhận. record là biên bản đang mở của kho (null khi chưa có), turn là lượt đang hiệu lực
    /// của người nhận trong biên bản đó (null khi chưa có), latestSubmission là hồ sơ danh tính (ảnh căn cước) mới nhất
    /// chưa bị thay thế của lượt đó (null khi chưa nộp).
    /// </summary>
    public static string ResolveStatus(
        HandoverRecord? record, Guid beneficiaryId, HandoverTurn? turn, HandoverSubmission? latestSubmission)
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

        // 3. Biên bản đang lập: luật sư đang xử lý
        if (record.Status == HandoverRecordStatus.Drafting)
        {
            return InReview;
        }

        // 4. Biên bản đã chốt mà không có tên tôi
        if (!inRecord)
        {
            if (record.Status == HandoverRecordStatus.Completed)
            {
                return Rejected;
            }
            return InReview;
        }

        // 5. Có tên tôi: đi theo lượt của chính tôi, không chờ người nhận khác
        if (turn == null)
        {
            return InReview;
        }
        if (turn.ReceivedAllAt != null)
        {
            return Received;
        }
        if (turn.Grant != null)
        {
            return CodeReady;
        }
        if (!IsIdentityConfirmed(turn))
        {
            if (NeedsIdentitySubmission(latestSubmission))
            {
                return NeedIdentity;
            }
            return InReview;
        }
        // Thứ tự ký phần của tôi: luật sư → Người thực thi → tôi. Chỉ báo cần ký khi tôi ký được ngay (cùng luật với lúc ký).
        string? blockReason = HandoverRecordRules.FindSignBlockReason(
            record, HandoverSignerRole.Recipient, beneficiaryId, true);
        if (blockReason == null)
        {
            return NeedMySignature;
        }
        // Còn chờ luật sư hoặc Người thực thi ký phần của tôi, hoặc tôi đã ký mà chưa được mở quyền tải.
        return WaitingOthers;
    }

    /// <summary>Danh tính của lượt đã được luật sư xác nhận (lượt đã sẵn sàng hoặc đã giao, hoặc có mốc xác nhận).</summary>
    private static bool IsIdentityConfirmed(HandoverTurn turn)
    {
        if (HandoverRecordRules.IsIdentityConfirmed(turn))
        {
            return true;
        }
        return turn.IdentityConfirmedAt != null;
    }

    /// <summary>
    /// Lượt chưa xác nhận danh tính: người nhận phải nộp ảnh căn cước khi chưa có hồ sơ, hoặc hồ sơ mới nhất bị yêu cầu
    /// bổ sung. Đã nộp và đang chờ duyệt thì không cần làm gì.
    /// </summary>
    private static bool NeedsIdentitySubmission(HandoverSubmission? latestSubmission)
    {
        if (latestSubmission == null)
        {
            return true;
        }
        return latestSubmission.Status == HandoverSubmissionStatus.SupplementRequested;
    }

    /// <summary>
    /// Có cần người nhận làm gì không. NOT_REQUESTED chỉ cần khi kho đã có biên bản đang mở chưa hoàn tất (người khác đã
    /// gửi yêu cầu); SUPPLEMENT_REQUESTED, NEED_IDENTITY, NEED_MY_SIGNATURE, CODE_READY luôn cần.
    /// </summary>
    public static bool NeedsAction(string status, HandoverRecord? record)
    {
        if (status == SupplementRequested || status == NeedIdentity || status == NeedMySignature || status == CodeReady)
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
    /// phải đang hoạt động; có biên bản thì biên bản chưa hoàn tất. Chỉ áp dụng khi trạng thái là NOT_REQUESTED hoặc REJECTED
    /// (yêu cầu đã bị từ chối, được gửi lại).
    /// </summary>
    public static bool CanRequest(string status, PlanStatus planStatus, HandoverRecord? record)
    {
        // Yêu cầu bị từ chối là đã đóng: người nhận được gửi yêu cầu mới (biên bản chưa hoàn tất).
        if (status != NotRequested && status != Rejected)
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
