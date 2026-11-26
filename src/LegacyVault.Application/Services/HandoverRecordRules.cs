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

    /// <summary>
    /// Chữ ký hiện tại của một bên cho phần của một người nhận. Mỗi bên ký riêng cho từng người nhận. Chữ ký của luật sư
    /// và Người thực thi không ghi người nhận (biên bản ký trước khi đổi cách ký) được tính cho mọi người nhận.
    /// </summary>
    public static HandoverRecordSignature? FindSignature(HandoverRecord record, HandoverSignerRole role, Guid? beneficiaryId)
    {
        HandoverRecordSignature? forAll = null;
        foreach (HandoverRecordSignature signature in CurrentSignatures(record))
        {
            if (signature.Role != role)
            {
                continue;
            }
            if (signature.BeneficiaryId == beneficiaryId)
            {
                return signature;
            }
            if (role != HandoverSignerRole.Recipient && signature.BeneficiaryId == null)
            {
                forAll = signature;
            }
        }
        return forAll;
    }

    /// <summary>
    /// Bên này đã ký xong cho mọi người nhận chưa: trả chữ ký muộn nhất; còn thiếu người nào thì null.
    /// </summary>
    public static HandoverRecordSignature? FindSignatureForAll(HandoverRecord record, HandoverSignerRole role)
    {
        HandoverRecordSignature? latest = null;
        foreach (Guid beneficiaryId in GroupAssetsByRecipient(record.Lines).Keys)
        {
            HandoverRecordSignature? signature = FindSignature(record, role, beneficiaryId);
            if (signature == null)
            {
                return null;
            }
            if (latest == null || signature.SignedAt > latest.SignedAt)
            {
                latest = signature;
            }
        }
        return latest;
    }

    /// <summary>Danh tính đã được luật sư xác nhận: lượt đã qua bước xác minh (hoặc đã bàn giao).</summary>
    public static bool IsIdentityConfirmed(HandoverTurn turn)
    {
        return turn.Status == HandoverTurnStatus.ReadyToAccept
            || turn.Status == HandoverTurnStatus.HandoverCommitted;
    }

    /// <summary>
    /// Biên bản đã chốt và đang giao: luật sư, Người thực thi ký được; mỗi người nhận ký khi danh tính của mình xong.
    /// Signing là trạng thái của biên bản chốt từ nay; IdentityCheck chỉ còn ở biên bản chốt trước khi giao riêng từng người.
    /// </summary>
    public static bool IsOpenForSigning(HandoverRecord record)
    {
        return record.Status == HandoverRecordStatus.Signing || record.Status == HandoverRecordStatus.IdentityCheck;
    }

    /// <summary>Khoảng cách tối thiểu giữa hai lần luật sư nhắc Người thực thi ký (giờ).</summary>
    public const int ExecutorReminderIntervalHours = 24;

    /// <summary>
    /// Lúc luật sư được nhắc Người thực thi lại: lần nhắc trước + 24 giờ. Null khi chưa nhắc lần nào (nhắc được ngay).
    /// </summary>
    public static DateTimeOffset? ExecutorRemindAvailableAt(HandoverRecord record)
    {
        if (record.ExecutorReminderSentAt == null)
        {
            return null;
        }
        return record.ExecutorReminderSentAt.Value.AddHours(ExecutorReminderIntervalHours);
    }

    /// <summary>
    /// Luật sư nhắc được Người thực thi lúc now chưa: chưa nhắc lần nào, hoặc lần nhắc trước đã quá 24 giờ.
    /// Hàm này chỉ xét thời gian; trạng thái biên bản và chữ ký do người gọi kiểm trước.
    /// </summary>
    public static bool CanRemindExecutor(HandoverRecord record, DateTimeOffset now)
    {
        DateTimeOffset? availableAt = ExecutorRemindAvailableAt(record);
        if (availableAt == null)
        {
            return true;
        }
        return now >= availableAt.Value;
    }

    /// <summary>Lý do chưa ký được: bên này đã ký phần này rồi (ký lặp thì giữ chữ ký cũ, không báo lỗi).</summary>
    public const string SignAlreadySigned = "ALREADY_SIGNED";

    /// <summary>Lý do chưa ký được: biên bản chưa chốt.</summary>
    public const string SignNotSigningStep = "NOT_SIGNING_STEP";

    /// <summary>Lý do chưa ký được: biên bản đã đủ chữ ký.</summary>
    public const string SignRecordCompleted = "RECORD_COMPLETED";

    /// <summary>Lý do luật sư, Người thực thi chưa ký được: danh tính người nhận chưa được xác nhận.</summary>
    public const string SignRecipientNotConfirmed = "RECIPIENT_NOT_CONFIRMED";

    /// <summary>Lý do người nhận chưa ký được: luật sư chưa xác nhận danh tính của chính người đó.</summary>
    public const string SignIdentityNotConfirmed = "IDENTITY_NOT_CONFIRMED";

    /// <summary>Lý do Người thực thi chưa ký được: luật sư chưa ký phần của người nhận này.</summary>
    public const string SignVerifierNotSigned = "VERIFIER_NOT_SIGNED";

    /// <summary>Lý do người nhận chưa ký được: Người thực thi chưa ký phần của người đó.</summary>
    public const string SignExecutorNotSigned = "EXECUTOR_NOT_SIGNED";

    /// <summary>
    /// Luật ký duy nhất "bên này ký phần của người nhận X được chưa", dùng chung cho lúc ký và cho cờ canSign trả về FE.
    /// Thứ tự ký cho phần của mỗi người nhận: luật sư → Người thực thi → người nhận. Trả null khi ký được ngay,
    /// ngược lại trả mã lý do (các hằng Sign... ở trên), xét lần lượt:
    /// 1. Biên bản chưa chốt hoặc đã đủ chữ ký.
    /// 2. Bên này đã ký phần X (kể cả chữ ký cũ không ghi người nhận của luật sư, Người thực thi trước khi ký riêng).
    /// 3. Danh tính của X chưa được luật sư xác nhận (identityConfirmed do người gọi đọc từ lượt của X).
    /// 4. Bên ký trước chưa ký phần X: Người thực thi chờ luật sư, người nhận chờ Người thực thi.
    /// Chữ ký đã có từ trước theo thứ tự khác (biên bản đang dở) được giữ nguyên; luật này chỉ áp cho lần ký sau.
    /// </summary>
    public static string? FindSignBlockReason(
        HandoverRecord record, HandoverSignerRole role, Guid beneficiaryId, bool identityConfirmed)
    {
        // 1. Biên bản phải đang ở bước ký
        if (!IsOpenForSigning(record))
        {
            if (record.Status == HandoverRecordStatus.Completed)
            {
                return SignRecordCompleted;
            }
            return SignNotSigningStep;
        }

        // 2. Bên này đã ký phần X
        if (FindSignature(record, role, beneficiaryId) != null)
        {
            return SignAlreadySigned;
        }

        // 3. Danh tính của X phải xong trước
        if (!identityConfirmed)
        {
            if (role == HandoverSignerRole.Recipient)
            {
                return SignIdentityNotConfirmed;
            }
            return SignRecipientNotConfirmed;
        }

        // 4. Bên ký trước trong thứ tự phải đã ký phần X
        if (role == HandoverSignerRole.Executor)
        {
            if (FindSignature(record, HandoverSignerRole.Verifier, beneficiaryId) == null)
            {
                return SignVerifierNotSigned;
            }
        }
        if (role == HandoverSignerRole.Recipient)
        {
            if (FindSignature(record, HandoverSignerRole.Executor, beneficiaryId) == null)
            {
                return SignExecutorNotSigned;
            }
        }
        return null;
    }

    /// <summary>
    /// Che số căn cước cho Người thực thi: chỉ giữ 4 số cuối ("•••• 1793"). Rỗng thì trả rỗng; ngắn hơn 4 ký tự thì che hết.
    /// </summary>
    public static string MaskCitizenId(string citizenId)
    {
        if (string.IsNullOrEmpty(citizenId))
        {
            return string.Empty;
        }
        if (citizenId.Length < 4)
        {
            return "••••";
        }
        return "•••• " + citizenId.Substring(citizenId.Length - 4);
    }

    /// <summary>Câu báo lỗi (409 HANDOVER_NOT_ALLOWED) cho một mã lý do chưa ký được.</summary>
    public static string SignBlockMessage(string reason)
    {
        if (reason == SignRecordCompleted)
        {
            return "Biên bản đã đủ chữ ký.";
        }
        if (reason == SignNotSigningStep)
        {
            return "Chưa tới bước ký: biên bản phải được chốt.";
        }
        if (reason == SignAlreadySigned)
        {
            return "Bạn đã ký phần này.";
        }
        if (reason == SignRecipientNotConfirmed)
        {
            return "Chưa ký được: người nhận này chưa được xác nhận danh tính.";
        }
        if (reason == SignIdentityNotConfirmed)
        {
            return "Chưa ký được: luật sư chưa xác nhận danh tính của bạn. Hãy nộp ảnh căn cước ở cổng người nhận.";
        }
        if (reason == SignVerifierNotSigned)
        {
            return "Luật sư chưa ký phần của người nhận này.";
        }
        if (reason == SignExecutorNotSigned)
        {
            return "Người thực thi chưa ký phần của bạn.";
        }
        return "Chưa ký được phần này.";
    }

    /// <summary>
    /// Có phần nào luật sư đã ký mà Người thực thi chưa ký không (bản hiện tại, biên bản đang ở bước ký). Luật sư chỉ nhắc
    /// được Người thực thi khi có phần như vậy: phần luật sư chưa ký thì Người thực thi chưa ký được.
    /// </summary>
    public static bool HasPartWaitingForExecutor(HandoverRecord record)
    {
        if (!IsOpenForSigning(record))
        {
            return false;
        }
        foreach (Guid beneficiaryId in GroupAssetsByRecipient(record.Lines).Keys)
        {
            bool verifierSigned = FindSignature(record, HandoverSignerRole.Verifier, beneficiaryId) != null;
            bool executorSigned = FindSignature(record, HandoverSignerRole.Executor, beneficiaryId) != null;
            if (verifierSigned && !executorSigned)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Biên bản đã chốt, chưa hoàn tất và có người nhận chỉ còn thiếu chữ ký luật sư để được mở quyền tải: Người thực thi
    /// và người nhận đó đã ký, luật sư chưa ký cho người đó (đều tính theo bản hiện tại).
    /// Theo thứ tự ký mới (luật sư ký trước) tình huống này chỉ còn gặp ở biên bản đang dở có chữ ký sai thứ tự từ trước
    /// khi đổi luật; vẫn giữ để báo luật sư ký nốt những phần đó.
    /// </summary>
    public static bool OnlyVerifierSignatureMissing(HandoverRecord record)
    {
        if (!IsOpenForSigning(record))
        {
            return false;
        }
        foreach (Guid beneficiaryId in GroupAssetsByRecipient(record.Lines).Keys)
        {
            if (FindSignature(record, HandoverSignerRole.Verifier, beneficiaryId) == null
                && FindSignature(record, HandoverSignerRole.Executor, beneficiaryId) != null
                && FindSignature(record, HandoverSignerRole.Recipient, beneficiaryId) != null)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Đủ chữ ký ba bên của bản hiện tại cho phần của một người nhận.</summary>
    public static bool IsSignedFor(HandoverRecord record, Guid beneficiaryId)
    {
        return FindSignature(record, HandoverSignerRole.Verifier, beneficiaryId) != null
            && FindSignature(record, HandoverSignerRole.Executor, beneficiaryId) != null
            && FindSignature(record, HandoverSignerRole.Recipient, beneficiaryId) != null;
    }

    /// <summary>Phần của mọi người nhận trong biên bản đều đủ chữ ký ba bên.</summary>
    public static bool AllSigned(HandoverRecord record)
    {
        foreach (Guid beneficiaryId in GroupAssetsByRecipient(record.Lines).Keys)
        {
            if (!IsSignedFor(record, beneficiaryId))
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
    /// chấp nhận, hoặc biên bản được mở từ hồ sơ báo tin đã chấp nhận. Trả câu lỗi, hoặc null khi hợp lệ.
    /// </summary>
    public static string? ValidateClaimsForFinalize(List<HandoverClaim> claims, bool hasAcceptedReport = false)
    {
        bool anyAccepted = hasAcceptedReport;
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

    /// <summary>
    /// So dòng biên bản với cách chia ban đầu của Chủ tài sản (mỗi cặp người nhận - tài sản). Trả các khác biệt theo thứ tự:
    /// người nhận được thêm, người nhận bị bỏ, rồi tài sản đổi người nhận (thêm/bớt người cùng nhận). Rỗng khi giống hệt.
    /// Người gọi bỏ trước các dòng gán trỏ tới tài sản đã bị xóa nội dung (biên bản không ghi được tài sản đó).
    /// </summary>
    public static List<HandoverSplitDifference> FindSplitDifferences(
        List<(Guid BeneficiaryId, Guid AssetId)> recordLines, List<(Guid BeneficiaryId, Guid AssetId)> ownerLines)
    {
        Dictionary<Guid, List<Guid>> recordByAsset = GroupRecipientsByAsset(recordLines);
        Dictionary<Guid, List<Guid>> ownerByAsset = GroupRecipientsByAsset(ownerLines);
        List<Guid> recordRecipients = DistinctRecipients(recordLines);
        List<Guid> ownerRecipients = DistinctRecipients(ownerLines);
        List<HandoverSplitDifference> result = new List<HandoverSplitDifference>();

        // 1. Người nhận có trong biên bản mà Chủ tài sản không chia cho
        foreach (Guid beneficiaryId in recordRecipients)
        {
            if (!ownerRecipients.Contains(beneficiaryId))
            {
                HandoverSplitDifference added = new HandoverSplitDifference();
                added.Kind = HandoverSplitDifference.RecipientAdded;
                added.BeneficiaryId = beneficiaryId;
                result.Add(added);
            }
        }

        // 2. Người nhận Chủ tài sản đã chia mà biên bản bỏ ra
        foreach (Guid beneficiaryId in ownerRecipients)
        {
            if (!recordRecipients.Contains(beneficiaryId))
            {
                HandoverSplitDifference removed = new HandoverSplitDifference();
                removed.Kind = HandoverSplitDifference.RecipientRemoved;
                removed.BeneficiaryId = beneficiaryId;
                result.Add(removed);
            }
        }

        // 3. Tài sản có nhóm người nhận khác nhau (theo thứ tự tài sản của Chủ tài sản rồi tới tài sản chỉ có trong biên bản)
        List<Guid> assetIds = new List<Guid>(ownerByAsset.Keys);
        foreach (Guid assetId in recordByAsset.Keys)
        {
            if (!assetIds.Contains(assetId))
            {
                assetIds.Add(assetId);
            }
        }
        foreach (Guid assetId in assetIds)
        {
            List<Guid> ownerSet = new List<Guid>();
            if (ownerByAsset.ContainsKey(assetId))
            {
                ownerSet = ownerByAsset[assetId];
            }
            List<Guid> recordSet = new List<Guid>();
            if (recordByAsset.ContainsKey(assetId))
            {
                recordSet = recordByAsset[assetId];
            }
            if (SameMembers(ownerSet, recordSet))
            {
                continue;
            }
            HandoverSplitDifference changed = new HandoverSplitDifference();
            changed.Kind = HandoverSplitDifference.AssetRecipientsChanged;
            changed.AssetId = assetId;
            changed.OwnerBeneficiaryIds = new List<Guid>(ownerSet);
            changed.RecordBeneficiaryIds = new List<Guid>(recordSet);
            result.Add(changed);
        }
        return result;
    }

    private static Dictionary<Guid, List<Guid>> GroupRecipientsByAsset(List<(Guid BeneficiaryId, Guid AssetId)> lines)
    {
        Dictionary<Guid, List<Guid>> result = new Dictionary<Guid, List<Guid>>();
        foreach ((Guid beneficiaryId, Guid assetId) in lines)
        {
            if (!result.ContainsKey(assetId))
            {
                result[assetId] = new List<Guid>();
            }
            if (!result[assetId].Contains(beneficiaryId))
            {
                result[assetId].Add(beneficiaryId);
            }
        }
        return result;
    }

    private static List<Guid> DistinctRecipients(List<(Guid BeneficiaryId, Guid AssetId)> lines)
    {
        List<Guid> result = new List<Guid>();
        foreach ((Guid beneficiaryId, Guid _) in lines)
        {
            if (!result.Contains(beneficiaryId))
            {
                result.Add(beneficiaryId);
            }
        }
        return result;
    }

    private static bool SameMembers(List<Guid> first, List<Guid> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }
        foreach (Guid item in first)
        {
            if (!second.Contains(item))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Lý do ghi vào biên bản khi hệ thống tự đóng vì mọi yêu cầu, hồ sơ đều bị luật sư từ chối.</summary>
    public const string AllRejectedReason = "Mọi yêu cầu bị từ chối";

    /// <summary>
    /// Kho đã khóa cho bàn giao chưa: khóa từ lúc luật sư chấp nhận giấy tờ đầu tiên (HandoverRequested), qua lúc chốt
    /// biên bản (HandoverInProgress) tới khi đóng (Closed). Có biên bản đang lập chưa chắc đã khóa: người nhận gửi yêu cầu
    /// chỉ tạo biên bản, Chủ tài sản vẫn sửa được kho cho tới khi luật sư chấp nhận.
    /// </summary>
    public static bool IsVaultLocked(PlanStatus planStatus)
    {
        if (planStatus == PlanStatus.HandoverRequested
            || planStatus == PlanStatus.HandoverInProgress
            || planStatus == PlanStatus.Closed)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Luật sư chấp nhận một yêu cầu hoặc hồ sơ báo tin: kế hoạch đang hoạt động thì khóa kho (HandoverRequested).
    /// Trả true khi lần này vừa khóa (người gọi báo Chủ tài sản và Người thực thi); kho đã khóa từ trước thì trả false.
    /// </summary>
    public static bool LockVaultOnAccept(HandoverPlan plan, DateTimeOffset now)
    {
        if (plan.Status != PlanStatus.Active)
        {
            return false;
        }
        plan.Status = PlanStatus.HandoverRequested;
        plan.UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Biên bản đang lập mà kho chưa khóa (chưa chấp nhận giấy tờ nào), mọi yêu cầu đã bị từ chối và không còn hồ sơ báo tin
    /// nào của kho chờ xét: biên bản không còn ai để giao, phải đóng để lần gửi sau tạo biên bản mới.
    /// </summary>
    public static bool ShouldCloseAfterAllRejected(HandoverRecord record, PlanStatus planStatus, int pendingReportCount)
    {
        if (record.Status != HandoverRecordStatus.Drafting)
        {
            return false;
        }
        if (IsVaultLocked(planStatus))
        {
            return false;
        }
        if (pendingReportCount > 0)
        {
            return false;
        }
        foreach (HandoverClaim claim in record.Claims)
        {
            if (claim.Status != HandoverClaimStatus.Rejected)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Đóng biên bản chưa khóa kho vì mọi yêu cầu bị từ chối (chưa có lượt nào vì chưa chốt). Chưa lưu.</summary>
    public static void CloseAfterAllRejected(HandoverRecord record, DateTimeOffset now)
    {
        record.Status = HandoverRecordStatus.Cancelled;
        record.CancelledAt = now;
        record.CancelReason = AllRejectedReason;
        record.UpdatedAt = now;
    }

    /// <summary>Người nhận có trong biên bản (đã gửi yêu cầu hoặc được ghi tài sản) không.</summary>
    public static bool HasRecipient(HandoverRecord record, Guid beneficiaryId)
    {
        foreach (HandoverClaim claim in record.Claims)
        {
            if (claim.BeneficiaryId == beneficiaryId)
            {
                return true;
            }
        }
        foreach (HandoverRecordLine line in record.Lines)
        {
            if (line.BeneficiaryId == beneficiaryId)
            {
                return true;
            }
        }
        return false;
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
