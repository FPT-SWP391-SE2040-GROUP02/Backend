using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Luật thứ tự ký phần của mỗi người nhận (luật sư → Người thực thi → người nhận) và luật nhắc Người thực thi.
/// Hàm thuần, không cần SQL.
/// </summary>
public sealed class HandoverSignOrderRulesTests
{
    private static readonly Guid RecipientA = Guid.NewGuid();

    private static readonly Guid RecipientB = Guid.NewGuid();

    private static HandoverRecord SigningRecord()
    {
        HandoverRecord record = new HandoverRecord();
        record.Status = HandoverRecordStatus.Signing;
        record.RevisionNo = 1;
        HandoverRecordLine lineA = new HandoverRecordLine();
        lineA.BeneficiaryId = RecipientA;
        lineA.AssetId = Guid.NewGuid();
        record.Lines.Add(lineA);
        HandoverRecordLine lineB = new HandoverRecordLine();
        lineB.BeneficiaryId = RecipientB;
        lineB.AssetId = Guid.NewGuid();
        record.Lines.Add(lineB);
        return record;
    }

    private static void Sign(HandoverRecord record, HandoverSignerRole role, Guid? beneficiaryId)
    {
        HandoverRecordSignature signature = new HandoverRecordSignature();
        signature.Role = role;
        signature.BeneficiaryId = beneficiaryId;
        signature.RevisionNo = record.RevisionNo;
        signature.SignedAt = DateTimeOffset.UtcNow;
        record.Signatures.Add(signature);
    }

    private static string? Reason(HandoverRecord record, HandoverSignerRole role, Guid beneficiaryId)
    {
        return HandoverRecordRules.FindSignBlockReason(record, role, beneficiaryId, true);
    }

    [Fact]
    public void FreshPart_OnlyVerifierCanSign_OthersWaitForThePreviousSigner()
    {
        // Chuẩn bị
        HandoverRecord record = SigningRecord();

        // Hành động
        string? verifier = Reason(record, HandoverSignerRole.Verifier, RecipientA);
        string? executor = Reason(record, HandoverSignerRole.Executor, RecipientA);
        string? recipient = Reason(record, HandoverSignerRole.Recipient, RecipientA);

        // Kiểm tra
        Assert.Null(verifier);
        Assert.Equal(HandoverRecordRules.SignVerifierNotSigned, executor);
        Assert.Equal(HandoverRecordRules.SignExecutorNotSigned, recipient);
    }

    [Fact]
    public void SigningInOrder_UnlocksTheNextSigner_OnlyForThatPart()
    {
        // Chuẩn bị: luật sư rồi Người thực thi ký phần của A
        HandoverRecord record = SigningRecord();
        Sign(record, HandoverSignerRole.Verifier, RecipientA);
        string? executorAfterVerifier = Reason(record, HandoverSignerRole.Executor, RecipientA);
        Sign(record, HandoverSignerRole.Executor, RecipientA);

        // Hành động
        string? recipientA = Reason(record, HandoverSignerRole.Recipient, RecipientA);
        string? executorB = Reason(record, HandoverSignerRole.Executor, RecipientB);
        string? recipientB = Reason(record, HandoverSignerRole.Recipient, RecipientB);
        string? verifierAgain = Reason(record, HandoverSignerRole.Verifier, RecipientA);

        // Kiểm tra: A tới lượt người nhận; B không bị ảnh hưởng; luật sư ký lại phần A là đã ký
        Assert.Null(executorAfterVerifier);
        Assert.Null(recipientA);
        Assert.Equal(HandoverRecordRules.SignVerifierNotSigned, executorB);
        Assert.Equal(HandoverRecordRules.SignExecutorNotSigned, recipientB);
        Assert.Equal(HandoverRecordRules.SignAlreadySigned, verifierAgain);
    }

    [Fact]
    public void IdentityNotConfirmed_ComesBeforeOrderReasons()
    {
        // Chuẩn bị
        HandoverRecord record = SigningRecord();

        // Hành động
        string? verifier = HandoverRecordRules.FindSignBlockReason(record, HandoverSignerRole.Verifier, RecipientA, false);
        string? executor = HandoverRecordRules.FindSignBlockReason(record, HandoverSignerRole.Executor, RecipientA, false);
        string? recipient = HandoverRecordRules.FindSignBlockReason(record, HandoverSignerRole.Recipient, RecipientA, false);

        // Kiểm tra
        Assert.Equal(HandoverRecordRules.SignRecipientNotConfirmed, verifier);
        Assert.Equal(HandoverRecordRules.SignRecipientNotConfirmed, executor);
        Assert.Equal(HandoverRecordRules.SignIdentityNotConfirmed, recipient);
    }

    [Fact]
    public void RecordNotFinalizedOrCompleted_NobodyCanSign()
    {
        // Chuẩn bị
        HandoverRecord drafting = SigningRecord();
        drafting.Status = HandoverRecordStatus.Drafting;
        HandoverRecord completed = SigningRecord();
        completed.Status = HandoverRecordStatus.Completed;

        // Hành động
        string? draftingReason = Reason(drafting, HandoverSignerRole.Verifier, RecipientA);
        string? completedReason = Reason(completed, HandoverSignerRole.Recipient, RecipientA);

        // Kiểm tra
        Assert.Equal(HandoverRecordRules.SignNotSigningStep, draftingReason);
        Assert.Equal(HandoverRecordRules.SignRecordCompleted, completedReason);
    }

    [Fact]
    public void OldSignatures_WithoutRecipient_StillCount_AndOutOfOrderSignaturesAreKept()
    {
        // Chuẩn bị: biên bản trước khi ký riêng (chữ ký luật sư không ghi người nhận) và biên bản đang dở có chữ ký
        // Người thực thi từ trước khi có luật thứ tự (luật sư chưa ký phần B)
        HandoverRecord record = SigningRecord();
        Sign(record, HandoverSignerRole.Verifier, null);
        HandoverRecord outOfOrder = SigningRecord();
        Sign(outOfOrder, HandoverSignerRole.Executor, RecipientB);

        // Hành động
        string? executorA = Reason(record, HandoverSignerRole.Executor, RecipientA);
        string? recipientB = Reason(outOfOrder, HandoverSignerRole.Recipient, RecipientB);
        string? verifierB = Reason(outOfOrder, HandoverSignerRole.Verifier, RecipientB);

        // Kiểm tra: chữ ký chung của luật sư tính cho A; chữ ký sai thứ tự được giữ và tính như đã ký
        Assert.Null(executorA);
        Assert.Null(recipientB);
        Assert.Null(verifierB);
    }

    [Fact]
    public void HasPartWaitingForExecutor_OnlyWhenVerifierSignedAPartTheExecutorHasNot()
    {
        // Chuẩn bị
        HandoverRecord record = SigningRecord();
        bool beforeVerifier = HandoverRecordRules.HasPartWaitingForExecutor(record);
        Sign(record, HandoverSignerRole.Verifier, RecipientA);
        bool afterVerifier = HandoverRecordRules.HasPartWaitingForExecutor(record);

        // Hành động
        Sign(record, HandoverSignerRole.Executor, RecipientA);
        bool afterExecutor = HandoverRecordRules.HasPartWaitingForExecutor(record);

        // Kiểm tra
        Assert.False(beforeVerifier);
        Assert.True(afterVerifier);
        Assert.False(afterExecutor);
    }
}
