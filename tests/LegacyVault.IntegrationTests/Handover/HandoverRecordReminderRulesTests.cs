using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Luật nhắc Người thực thi ký (24 giờ) và luật "chỉ còn thiếu chữ ký luật sư". Hàm thuần, không cần SQL.</summary>
public sealed class HandoverRecordReminderRulesTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);

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

    private static void Sign(HandoverRecord record, HandoverSignerRole role, Guid? beneficiaryId, int revisionNo)
    {
        HandoverRecordSignature signature = new HandoverRecordSignature();
        signature.Role = role;
        signature.BeneficiaryId = beneficiaryId;
        signature.RevisionNo = revisionNo;
        signature.SignedAt = Now;
        record.Signatures.Add(signature);
    }

    [Fact]
    public void NeverReminded_CanRemindAtOnce_AndHasNoAvailableTime()
    {
        // Chuẩn bị
        HandoverRecord record = SigningRecord();

        // Hành động
        bool canRemind = HandoverRecordRules.CanRemindExecutor(record, Now);
        DateTimeOffset? availableAt = HandoverRecordRules.ExecutorRemindAvailableAt(record);

        // Kiểm tra
        Assert.True(canRemind);
        Assert.Null(availableAt);
    }

    [Fact]
    public void RemindedLessThan24HoursAgo_CannotRemind_AndAvailableAtIsSentAtPlus24Hours()
    {
        // Chuẩn bị
        HandoverRecord record = SigningRecord();
        record.ExecutorReminderSentAt = Now.AddHours(-23);

        // Hành động
        bool canRemind = HandoverRecordRules.CanRemindExecutor(record, Now);
        DateTimeOffset? availableAt = HandoverRecordRules.ExecutorRemindAvailableAt(record);

        // Kiểm tra
        Assert.False(canRemind);
        Assert.Equal(Now.AddHours(1), availableAt);
    }

    [Fact]
    public void Reminded24HoursAgo_CanRemindAgain()
    {
        // Chuẩn bị
        HandoverRecord record = SigningRecord();
        record.ExecutorReminderSentAt = Now.AddHours(-24);

        // Hành động
        bool canRemind = HandoverRecordRules.CanRemindExecutor(record, Now);

        // Kiểm tra
        Assert.True(canRemind);
    }

    [Fact]
    public void OnlyVerifierSignatureMissing_IsTrueWhenExecutorAndOneRecipientSigned()
    {
        // Chuẩn bị: người nhận B chưa ký nhưng người nhận A đã ký
        HandoverRecord record = SigningRecord();
        Sign(record, HandoverSignerRole.Executor, null, 1);
        Sign(record, HandoverSignerRole.Recipient, RecipientA, 1);

        // Hành động
        bool missing = HandoverRecordRules.OnlyVerifierSignatureMissing(record);

        // Kiểm tra
        Assert.True(missing);
    }

    [Fact]
    public void OnlyVerifierSignatureMissing_IsFalseWhenNoRecipientSignedYet()
    {
        // Chuẩn bị
        HandoverRecord record = SigningRecord();
        Sign(record, HandoverSignerRole.Executor, null, 1);

        // Hành động
        bool missing = HandoverRecordRules.OnlyVerifierSignatureMissing(record);

        // Kiểm tra
        Assert.False(missing);
    }

    [Fact]
    public void OnlyVerifierSignatureMissing_IsFalseWhenExecutorHasNotSigned()
    {
        // Chuẩn bị
        HandoverRecord record = SigningRecord();
        Sign(record, HandoverSignerRole.Recipient, RecipientA, 1);

        // Hành động
        bool missing = HandoverRecordRules.OnlyVerifierSignatureMissing(record);

        // Kiểm tra
        Assert.False(missing);
    }

    [Fact]
    public void OnlyVerifierSignatureMissing_IsFalseWhenVerifierAlreadySigned()
    {
        // Chuẩn bị
        HandoverRecord record = SigningRecord();
        Sign(record, HandoverSignerRole.Executor, null, 1);
        Sign(record, HandoverSignerRole.Recipient, RecipientA, 1);
        Sign(record, HandoverSignerRole.Verifier, null, 1);

        // Hành động
        bool missing = HandoverRecordRules.OnlyVerifierSignatureMissing(record);

        // Kiểm tra
        Assert.False(missing);
    }

    [Fact]
    public void OnlyVerifierSignatureMissing_IgnoresSignaturesOfOlderRevisions()
    {
        // Chuẩn bị: chữ ký ở bản 1, biên bản đã mở lại thành bản 2
        HandoverRecord record = SigningRecord();
        record.RevisionNo = 2;
        Sign(record, HandoverSignerRole.Executor, null, 1);
        Sign(record, HandoverSignerRole.Recipient, RecipientA, 1);

        // Hành động
        bool missing = HandoverRecordRules.OnlyVerifierSignatureMissing(record);

        // Kiểm tra
        Assert.False(missing);
    }

    [Fact]
    public void OnlyVerifierSignatureMissing_IsFalseWhenRecordIsNotFinalized()
    {
        // Chuẩn bị
        HandoverRecord record = SigningRecord();
        record.Status = HandoverRecordStatus.Drafting;
        Sign(record, HandoverSignerRole.Executor, null, 1);
        Sign(record, HandoverSignerRole.Recipient, RecipientA, 1);

        // Hành động
        bool missing = HandoverRecordRules.OnlyVerifierSignatureMissing(record);

        // Kiểm tra
        Assert.False(missing);
    }
}
