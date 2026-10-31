using System.Text;
using System.Text.RegularExpressions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>DEATH-14: luật dựng chuỗi được ký cho quyết định cuối của Verifier. Không cần SQL.</summary>
public sealed class DecisionSignatureRulesTests
{
    [Fact]
    public void Death14_PayloadIsSameForSameDecision()
    {
        // Chuẩn bị
        DeathCaseDecision decision = CreateDecision("Giấy chứng tử hợp lệ.");
        List<Guid> fileIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        // Hành động
        byte[] first = DecisionSignatureRules.BuildPayload(decision, fileIds);
        byte[] second = DecisionSignatureRules.BuildPayload(decision, fileIds);

        // Kiểm tra
        Assert.Equal(first, second);
        string text = Encoding.UTF8.GetString(first);
        Assert.StartsWith("LV-DEATH-DECISION|v1|" + decision.Id.ToString("N") + "|", text);
        Assert.Contains("|APPROVED|", text);
        Assert.Contains("|" + decision.DecidedAt.UtcTicks + "|", text);
    }

    [Fact]
    public void Death14_ChangingReasonChangesPayload()
    {
        // Chuẩn bị
        DeathCaseDecision decision = CreateDecision("Giấy chứng tử hợp lệ.");
        List<Guid> fileIds = new List<Guid> { Guid.NewGuid() };
        byte[] before = DecisionSignatureRules.BuildPayload(decision, fileIds);

        // Hành động
        decision.Reason = "Giấy chứng tử hợp lệ!";
        byte[] after = DecisionSignatureRules.BuildPayload(decision, fileIds);

        // Kiểm tra
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Death14_ChangingDecidedAtChangesPayload()
    {
        // Chuẩn bị
        DeathCaseDecision decision = CreateDecision("Giấy chứng tử hợp lệ.");
        List<Guid> fileIds = new List<Guid>();
        byte[] before = DecisionSignatureRules.BuildPayload(decision, fileIds);

        // Hành động: lùi một tick; cùng thời điểm nhưng khác múi giờ thì chuỗi không đổi
        DateTimeOffset original = decision.DecidedAt;
        decision.DecidedAt = original.AddTicks(1);
        byte[] shifted = DecisionSignatureRules.BuildPayload(decision, fileIds);
        decision.DecidedAt = original.ToOffset(TimeSpan.FromHours(7));
        byte[] sameInstant = DecisionSignatureRules.BuildPayload(decision, fileIds);

        // Kiểm tra
        Assert.NotEqual(before, shifted);
        Assert.Equal(before, sameInstant);
    }

    [Fact]
    public void Death14_ReasonWithPipeDoesNotBreakPayload()
    {
        // Chuẩn bị
        DeathCaseDecision decision = CreateDecision("Thiếu trang 2 | cần bản rõ hơn");
        decision.Kind = DeathCaseDecisionKind.Rejected;
        List<Guid> fileIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        // Hành động
        string text = Encoding.UTF8.GetString(DecisionSignatureRules.BuildPayload(decision, fileIds));
        string[] parts = text.Split('|');

        // Kiểm tra: luôn 10 phần, lý do giải mã lại đúng
        Assert.Equal(10, parts.Length);
        Assert.Equal("REJECTED", parts[5]);
        Assert.Equal(decision.Reason, Encoding.UTF8.GetString(Convert.FromBase64String(parts[8])));
        Assert.Equal(fileIds[0].ToString("N") + "," + fileIds[1].ToString("N"), parts[9]);
    }

    [Fact]
    public void Death14_FileOrderFollowsPosition()
    {
        // Chuẩn bị: thêm tệp lệch thứ tự Position
        DeathCaseVersion version = new DeathCaseVersion();
        DeathCaseFile third = CreateFile(3);
        DeathCaseFile first = CreateFile(1);
        DeathCaseFile second = CreateFile(2);
        version.Files.Add(third);
        version.Files.Add(first);
        version.Files.Add(second);
        DeathCaseDecision decision = CreateDecision("Giấy chứng tử hợp lệ.");

        // Hành động
        List<Guid> fileIds = DecisionSignatureRules.FileIdsInPosition(version);
        string text = Encoding.UTF8.GetString(DecisionSignatureRules.BuildPayload(decision, fileIds));
        List<Guid> noFiles = DecisionSignatureRules.FileIdsInPosition(new DeathCaseVersion());

        // Kiểm tra
        Assert.Equal(new List<Guid> { first.Id, second.Id, third.Id }, fileIds);
        Assert.EndsWith("|" + first.Id.ToString("N") + "," + second.Id.ToString("N") + "," + third.Id.ToString("N"), text);
        // Danh sách gốc của bản hồ sơ không bị sắp lại.
        Assert.Same(third, version.Files[0]);
        Assert.Empty(noFiles);
    }

    [Fact]
    public void Death14_FingerprintIsSixteenHexInGroups()
    {
        // Chuẩn bị
        byte[] publicKey = Encoding.UTF8.GetBytes("khoa-cong-khai-thu");
        byte[] otherKey = Encoding.UTF8.GetBytes("khoa-cong-khai-khac");

        // Hành động
        string fingerprint = DecisionSignatureRules.Fingerprint(publicKey);
        string again = DecisionSignatureRules.Fingerprint(publicKey);
        string other = DecisionSignatureRules.Fingerprint(otherKey);

        // Kiểm tra
        Assert.Matches(new Regex("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$"), fingerprint);
        Assert.Equal(fingerprint, again);
        Assert.NotEqual(fingerprint, other);
    }

    [Fact]
    public void Death14_SupplementRequestIsNotSignedKind()
    {
        // Chuẩn bị
        DeathCaseDecision decision = CreateDecision("Cần bổ sung bản rõ hơn.");
        decision.Kind = DeathCaseDecisionKind.SupplementRequested;

        // Hành động
        Exception? error = Record.Exception(() => DecisionSignatureRules.BuildPayload(decision, new List<Guid>()));

        // Kiểm tra
        Assert.False(DecisionSignatureRules.IsSignedKind(DeathCaseDecisionKind.SupplementRequested));
        Assert.True(DecisionSignatureRules.IsSignedKind(DeathCaseDecisionKind.Approved));
        Assert.True(DecisionSignatureRules.IsSignedKind(DeathCaseDecisionKind.Rejected));
        Assert.IsType<ArgumentException>(error);
    }

    private static DeathCaseDecision CreateDecision(string reason)
    {
        DeathCaseDecision decision = new DeathCaseDecision();
        decision.CaseId = Guid.NewGuid();
        decision.VersionNo = 2;
        decision.Kind = DeathCaseDecisionKind.Approved;
        decision.Reason = reason;
        decision.VerifierUserId = Guid.NewGuid();
        decision.AssignmentId = Guid.NewGuid();
        decision.DecidedAt = new DateTimeOffset(2026, 10, 30, 10, 15, 30, TimeSpan.Zero).AddTicks(1234567);
        return decision;
    }

    private static DeathCaseFile CreateFile(int position)
    {
        DeathCaseFile file = new DeathCaseFile();
        file.Position = position;
        return file;
    }
}
