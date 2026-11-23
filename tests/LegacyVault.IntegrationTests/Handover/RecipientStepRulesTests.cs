using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Bảng ánh xạ bước của người nhận (màn "Nhận tài sản"), không cần database.</summary>
public sealed class RecipientStepRulesTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static HandoverRecord Record(HandoverRecordStatus status)
    {
        HandoverRecord record = new HandoverRecord();
        record.Status = status;
        record.RevisionNo = 1;
        return record;
    }

    private static void AddClaim(HandoverRecord record, Guid beneficiaryId, HandoverClaimStatus status)
    {
        HandoverClaim claim = new HandoverClaim();
        claim.RecordId = record.Id;
        claim.BeneficiaryId = beneficiaryId;
        claim.Status = status;
        record.Claims.Add(claim);
    }

    private static void AddLine(HandoverRecord record, Guid beneficiaryId)
    {
        HandoverRecordLine line = new HandoverRecordLine();
        line.RecordId = record.Id;
        line.BeneficiaryId = beneficiaryId;
        line.AssetId = Guid.NewGuid();
        record.Lines.Add(line);
    }

    private static void AddMySignature(HandoverRecord record)
    {
        HandoverRecordSignature signature = new HandoverRecordSignature();
        signature.RecordId = record.Id;
        signature.RevisionNo = record.RevisionNo;
        signature.Role = HandoverSignerRole.Recipient;
        signature.BeneficiaryId = Me;
        record.Signatures.Add(signature);
    }

    [Fact]
    public void NoRecord_IsNotRequested_WithoutAction()
    {
        // Chuẩn bị
        HandoverRecord? record = null;

        // Hành động
        string status = RecipientStepRules.ResolveStatus(record, Me, null);

        // Kiểm tra
        Assert.Equal(RecipientStepRules.NotRequested, status);
        Assert.False(RecipientStepRules.NeedsAction(status, record));
        Assert.True(RecipientStepRules.CanRequest(status, PlanStatus.Active, record));
        Assert.False(RecipientStepRules.CanRequest(status, PlanStatus.Closed, record));
    }

    [Fact]
    public void SomeoneElseRequested_IsNotRequested_AndNeedsAction()
    {
        // Chuẩn bị
        HandoverRecord record = Record(HandoverRecordStatus.Drafting);
        AddClaim(record, Other, HandoverClaimStatus.Submitted);

        // Hành động
        string status = RecipientStepRules.ResolveStatus(record, Me, null);

        // Kiểm tra
        Assert.Equal(RecipientStepRules.NotRequested, status);
        Assert.True(RecipientStepRules.NeedsAction(status, record));
        Assert.True(RecipientStepRules.CanRequest(status, PlanStatus.HandoverRequested, record));
    }

    [Fact]
    public void CompletedWithoutMe_IsRejected_AndNothingToDo()
    {
        // Chuẩn bị: tôi được luật sư ghi tên rồi bị bỏ, biên bản hoàn tất không có tôi
        HandoverRecord record = Record(HandoverRecordStatus.Completed);
        AddClaim(record, Me, HandoverClaimStatus.Accepted);
        AddLine(record, Other);

        // Hành động
        string status = RecipientStepRules.ResolveStatus(record, Me, null);

        // Kiểm tra
        Assert.Equal(RecipientStepRules.Rejected, status);
        Assert.False(RecipientStepRules.NeedsAction(status, record));
    }

    [Theory]
    [InlineData(HandoverClaimStatus.Submitted, RecipientStepRules.RequestPending, false)]
    [InlineData(HandoverClaimStatus.SupplementRequested, RecipientStepRules.SupplementRequested, true)]
    [InlineData(HandoverClaimStatus.Rejected, RecipientStepRules.Rejected, false)]
    [InlineData(HandoverClaimStatus.Accepted, RecipientStepRules.InReview, false)]
    public void MyClaimStatus_MapsToStep(HandoverClaimStatus claimStatus, string expected, bool needsAction)
    {
        // Chuẩn bị
        HandoverRecord record = Record(HandoverRecordStatus.Drafting);
        AddClaim(record, Me, claimStatus);

        // Hành động
        string status = RecipientStepRules.ResolveStatus(record, Me, null);

        // Kiểm tra
        Assert.Equal(expected, status);
        Assert.Equal(needsAction, RecipientStepRules.NeedsAction(status, record));
        Assert.False(RecipientStepRules.CanRequest(status, PlanStatus.HandoverRequested, record));
    }

    [Fact]
    public void AddedByVerifierWithoutClaim_IsInReview()
    {
        // Chuẩn bị: luật sư thêm tôi vào biên bản, tôi chưa gửi yêu cầu
        HandoverRecord record = Record(HandoverRecordStatus.IdentityCheck);
        AddLine(record, Me);

        // Hành động
        string status = RecipientStepRules.ResolveStatus(record, Me, new HandoverTurn());

        // Kiểm tra
        Assert.Equal(RecipientStepRules.InReview, status);
        Assert.False(RecipientStepRules.NeedsAction(status, record));
    }

    [Fact]
    public void Signing_BeforeAndAfterMySignature()
    {
        // Chuẩn bị
        HandoverRecord record = Record(HandoverRecordStatus.Signing);
        AddClaim(record, Me, HandoverClaimStatus.Accepted);
        AddLine(record, Me);

        // Hành động
        string beforeSign = RecipientStepRules.ResolveStatus(record, Me, new HandoverTurn());
        AddMySignature(record);
        string afterSign = RecipientStepRules.ResolveStatus(record, Me, new HandoverTurn());

        // Kiểm tra
        Assert.Equal(RecipientStepRules.NeedMySignature, beforeSign);
        Assert.True(RecipientStepRules.NeedsAction(beforeSign, record));
        Assert.Equal(RecipientStepRules.WaitingOthers, afterSign);
        Assert.False(RecipientStepRules.NeedsAction(afterSign, record));
    }

    [Fact]
    public void Completed_CodeReadyThenReceived()
    {
        // Chuẩn bị
        HandoverRecord record = Record(HandoverRecordStatus.Completed);
        AddClaim(record, Me, HandoverClaimStatus.Accepted);
        AddLine(record, Me);
        HandoverTurn turn = new HandoverTurn();
        turn.Grant = new HandoverGrant();

        // Hành động
        string ready = RecipientStepRules.ResolveStatus(record, Me, turn);
        turn.ReceivedAllAt = DateTimeOffset.UtcNow;
        string received = RecipientStepRules.ResolveStatus(record, Me, turn);

        // Kiểm tra
        Assert.Equal(RecipientStepRules.CodeReady, ready);
        Assert.True(RecipientStepRules.NeedsAction(ready, record));
        Assert.Equal(RecipientStepRules.Received, received);
        Assert.False(RecipientStepRules.NeedsAction(received, record));
    }
}
