namespace LegacyVault.Domain.Enums;

/// <summary>Kết luận của Verifier về báo còn sống (ALIVE-05).</summary>
public enum ObjectionConclusion
{
    /// <summary>Owner còn sống: từ chối hồ sơ chứng tử, hủy lượt, thu hồi quyền, kế hoạch về ACTIVE.</summary>
    AliveConfirmed = 0,
    /// <summary>Phản đối không đúng: trả hồ sơ về trạng thái trước; KHÔNG tự duyệt chứng tử.</summary>
    ObjectionRejected = 1,
    /// <summary>Chưa đủ căn cứ: giữ chặn, ghi lý do và hạn mới cho Verifier.</summary>
    InsufficientEvidence = 2,
}
