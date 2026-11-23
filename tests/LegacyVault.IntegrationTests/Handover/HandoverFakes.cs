using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using LegacyVault.IntegrationTests.GoogleAuth;

namespace LegacyVault.IntegrationTests.Handover;

// Các class giả để test giai đoạn nhận tài sản mà không cần SQL Server. Dữ liệu chỉ nằm trong List.

public sealed class FakeHandoverRepository : IHandoverRepository
{
    public List<HandoverTurn> Turns { get; } = new List<HandoverTurn>();
    public List<HandoverGrant> Grants { get; } = new List<HandoverGrant>();
    public List<HandoverAccessLog> Logs { get; } = new List<HandoverAccessLog>();
    public List<Asset> Assets { get; } = new List<Asset>();

    public Task<HandoverTurn?> FindTurnByTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        return Task.FromResult(Turns.FirstOrDefault(t => t.InvitationTokenHash == tokenHash));
    }

    public Task<HandoverTurn?> FindTurnByIdAsync(Guid turnId, CancellationToken ct)
    {
        return Task.FromResult(Turns.FirstOrDefault(t => t.Id == turnId));
    }

    public Task<bool> HasOpenClaimedTurnAsync(Guid userId, CancellationToken ct)
    {
        bool open = false;
        foreach (HandoverTurn turn in Turns)
        {
            if (turn.ClaimantUserId == userId && !HandoverRules.IsTerminal(turn.Status))
            {
                open = true;
            }
        }
        return Task.FromResult(open);
    }

    public Task<List<HandoverTurn>> FindTurnsByClaimantAsync(Guid userId, CancellationToken ct)
    {
        List<HandoverTurn> mine = Turns
            .Where(t => t.ClaimantUserId == userId)
            .OrderByDescending(t => t.InvitedAt)
            .ThenByDescending(t => t.Id)
            .ToList();
        return Task.FromResult(mine);
    }

    public Task<List<HandoverTurn>> FindUnclaimedTurnsInHandoverAsync(CancellationToken ct)
    {
        List<HandoverTurn> found = Turns
            .Where(t => t.ClaimantUserId == null
                && (t.Status == HandoverTurnStatus.Invited || t.Status == HandoverTurnStatus.InitialResponseExpired))
            .OrderByDescending(t => t.InvitedAt)
            .ThenByDescending(t => t.Id)
            .ToList();
        return Task.FromResult(found);
    }

    public Task<List<HandoverTurn>> FindTurnsByPlanAsync(Guid planId, CancellationToken ct)
    {
        return Task.FromResult(Turns.Where(t => t.PlanId == planId).ToList());
    }

    public Task<HandoverGrant?> FindGrantByIdAsync(Guid grantId, CancellationToken ct)
    {
        return Task.FromResult(Grants.FirstOrDefault(g => g.Id == grantId));
    }

    public Task<Asset?> FindAssetAsync(Guid assetId, CancellationToken ct)
    {
        return Task.FromResult(Assets.FirstOrDefault(a => a.Id == assetId));
    }

    public Task<IReadOnlyList<Guid>> FindUndeliveredInvitationTurnIdsAsync(
        DateTimeOffset createdBefore, int maxAttempts, CancellationToken ct)
    {
        IReadOnlyList<Guid> ids = Turns
            .Where(t => t.Status == HandoverTurnStatus.Invited
                && t.InvitationSentAt == null
                && t.ClaimantUserId == null
                && t.CreatedAt <= createdBefore
                && t.InvitationAttempts < maxAttempts)
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .ToList();
        return Task.FromResult(ids);
    }

    public Task<IReadOnlyList<Guid>> FindExpiredResponseTurnIdsAsync(DateTimeOffset now, CancellationToken ct)
    {
        IReadOnlyList<Guid> ids = Turns
            .Where(t => t.Status == HandoverTurnStatus.Invited && t.ResponseDeadline <= now)
            .OrderBy(t => t.ResponseDeadline)
            .Select(t => t.Id)
            .ToList();
        return Task.FromResult(ids);
    }

    public Task<IReadOnlyList<Guid>> FindExpiredDecisionTurnIdsAsync(DateTimeOffset now, CancellationToken ct)
    {
        IReadOnlyList<Guid> ids = Turns
            .Where(t => t.Status == HandoverTurnStatus.ReadyToAccept && t.DecisionDeadline != null && t.DecisionDeadline <= now)
            .OrderBy(t => t.DecisionDeadline)
            .Select(t => t.Id)
            .ToList();
        return Task.FromResult(ids);
    }

    public Task<IReadOnlyList<Guid>> FindExpiredGrantIdsAsync(DateTimeOffset now, CancellationToken ct)
    {
        IReadOnlyList<Guid> ids = Grants
            .Where(g => g.Status == HandoverGrantStatus.Active && g.ExpiresAt <= now)
            .OrderBy(g => g.ExpiresAt)
            .Select(g => g.Id)
            .ToList();
        return Task.FromResult(ids);
    }

    public bool IsUniqueViolation(Exception exception)
    {
        return exception is Microsoft.EntityFrameworkCore.DbUpdateException;
    }

    public void AddTurn(HandoverTurn turn)
    {
        Turns.Add(turn);
    }

    public void AddGrant(HandoverGrant grant)
    {
        Grants.Add(grant);
        // Giống EF: thêm grant thì fix-up gắn vào lượt.
        HandoverTurn? turn = Turns.FirstOrDefault(t => t.Id == grant.TurnId);
        if (turn != null)
        {
            turn.Grant = grant;
            grant.Turn = turn;
        }
    }

    public void AddAccessLog(HandoverAccessLog log)
    {
        Logs.Add(log);
    }
}

public sealed class FakeInvitationSender : IHandoverInvitationSender
{
    private readonly FakeUnitOfWork? unitOfWork;

    public FakeInvitationSender()
    {
    }

    public FakeInvitationSender(FakeUnitOfWork unitOfWork)
    {
        this.unitOfWork = unitOfWork;
    }

    public List<HandoverInvitation> Sent { get; } = new List<HandoverInvitation>();

    /// <summary>SaveCount của unit of work tại lúc mỗi lời mời được gửi.</summary>
    public List<int> SaveCountsAtSend { get; } = new List<int>();

    public bool FailNext { get; set; }

    public Task SendAsync(HandoverInvitation invitation, CancellationToken ct)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new InvalidOperationException("Gửi lỗi giả lập.");
        }

        Sent.Add(invitation);
        if (unitOfWork != null)
        {
            SaveCountsAtSend.Add(unitOfWork.SaveCount);
        }
        return Task.CompletedTask;
    }
}

public sealed class FakeBeneficiaryDataProtector : IBeneficiaryDataProtector
{
    public Dictionary<Guid, BeneficiaryPersonalData> Data { get; } = new Dictionary<Guid, BeneficiaryPersonalData>();

    /// <summary>Đặt true để mô phỏng không giải mã được (thiếu KEK, dữ liệu hỏng).</summary>
    public bool Failing { get; set; }

    public void Protect(Beneficiary beneficiary, BeneficiaryPersonalData data)
    {
    }

    public BeneficiaryPersonalData Unprotect(Beneficiary beneficiary)
    {
        if (Failing)
        {
            throw new System.Security.Cryptography.CryptographicException("Không giải mã được.");
        }
        return Data[beneficiary.Id];
    }
}


public sealed class FakePolicy : IHandoverPolicy
{
    public bool RequireIdentityVerification { get; set; } = true;
}

/// <summary>Dựng kho, kế hoạch, tài sản, người nhận, dòng gán và lượt cho test.</summary>
public sealed class HandoverTestData
{
    public HandoverTestData(DateTimeOffset now)
    {
        Now = now;
        Owner = new User { Email = "owner@example.com", FullName = "Chủ Kho", Status = UserStatus.Active, EmailVerifiedAt = now.AddDays(-100), Role = UserRole.User };
        Vault = new Vault { OwnerId = Owner.Id, Owner = Owner, Name = "Kho", Status = VaultStatus.Open, CreatedAt = now.AddDays(-100) };
        Vault.Plan = new HandoverPlan { VaultId = Vault.Id, Vault = Vault, Status = PlanStatus.HandoverRequested, CreatedAt = now.AddDays(-100) };
    }

    public DateTimeOffset Now { get; }
    public User Owner { get; }
    public Vault Vault { get; }
    public HandoverPlan Plan
    {
        get { return Vault.Plan; }
    }

    public List<AssetAssignment> Assignments { get; } = new List<AssetAssignment>();
    public List<Asset> Assets { get; } = new List<Asset>();
    public Dictionary<Guid, BeneficiaryPersonalData> People { get; } = new Dictionary<Guid, BeneficiaryPersonalData>();

    /// <summary>Thêm một tài sản có sẵn nội dung giả (mỗi tài sản một nội dung).</summary>
    public Asset AddAsset(string name)
    {
        int number = Assets.Count + 1;
        Asset asset = new Asset
        {
            VaultId = Vault.Id,
            Vault = Vault,
            Kind = AssetKind.File,
            Name = name,
            FileName = "tep-" + number + ".bin",
            ContentType = "application/pdf",
            SizeBytes = 1000 + number,
            StorageKey = "key-" + Guid.NewGuid().ToString("N"),
            EncryptionKeyId = "k1",
            CreatedAt = Now.AddDays(-49)
        };
        Assets.Add(asset);
        return asset;
    }

    /// <summary>Thêm nhiều tài sản, tên theo dạng "prefix-1", "prefix-2"...</summary>
    public List<Asset> AddAssets(string prefix, int count)
    {
        List<Asset> added = new List<Asset>();
        for (int i = 0; i < count; i++)
        {
            added.Add(AddAsset(prefix + "-" + (i + 1)));
        }
        return added;
    }

    /// <summary>Thêm một hồ sơ người nhận mới.</summary>
    public Beneficiary AddRecipient(string fullName, string? email)
    {
        Beneficiary beneficiary = new Beneficiary { VaultId = Vault.Id, Vault = Vault, CitizenIdLast4 = "1234", CreatedAt = Now.AddDays(-40) };
        BeneficiaryPersonalData data = new BeneficiaryPersonalData
        {
            CitizenId = "012345671234",
            FullName = fullName,
            DateOfBirth = new DateOnly(1990, 1, 1),
            Email = email
        };
        People[beneficiary.Id] = data;
        return beneficiary;
    }

    /// <summary>Gán một tài sản cho một người nhận.</summary>
    public AssetAssignment Assign(Asset asset, Beneficiary recipient)
    {
        AssetAssignment assignment = new AssetAssignment
        {
            AssetId = asset.Id,
            Asset = asset,
            BeneficiaryId = recipient.Id,
            Beneficiary = recipient,
            CreatedAt = Now.AddDays(-30).AddMinutes(Assignments.Count)
        };
        Assignments.Add(assignment);
        return assignment;
    }

    /// <summary>Gán mọi tài sản trong danh sách cho một người nhận.</summary>
    public void AssignAll(List<Asset> assets, Beneficiary recipient)
    {
        foreach (Asset asset in assets)
        {
            Assign(asset, recipient);
        }
    }

    /// <summary>Dựng lượt ở trạng thái cho trước (kế hoạch được đặt HandoverInProgress) với token cho trước; snapshot là các tài sản gán cho người nhận.</summary>
    public HandoverTurn BuildTurn(Beneficiary recipient, HandoverTurnStatus status, string token, Guid? claimant)
    {
        HandoverTurn turn = new HandoverTurn
        {
            VaultId = Vault.Id,
            PlanId = Plan.Id,
            ContainerName = "Biên bản bàn giao",
            BeneficiaryId = recipient.Id,
            Status = status,
            InvitationTokenHash = new FakeTokenService().HashOpaqueToken(token),
            InvitedAt = Now,
            ResponseDeadline = Now.AddDays(5),
            ClaimantUserId = claimant,
            CreatedAt = Now
        };

        int position = 0;
        foreach (AssetAssignment assignment in Assignments)
        {
            if (assignment.BeneficiaryId != recipient.Id)
            {
                continue;
            }
            Asset asset = assignment.Asset;
            turn.Items.Add(new HandoverSnapshotItem
            {
                TurnId = turn.Id,
                Turn = turn,
                AssetId = asset.Id,
                Name = asset.Name,
                Kind = asset.Kind,
                FileName = asset.FileName,
                ContentType = asset.ContentType,
                SizeBytes = asset.SizeBytes,
                Position = position
            });
            position++;
        }

        Plan.Status = PlanStatus.HandoverInProgress;
        return turn;
    }
}

/// <summary>Kho nội dung giả: trả byte theo StorageKey, có thể mô phỏng tệp hỏng ở khối cuối.</summary>
public sealed class FakeAssetContentStore : IAssetContentStore
{
    private const int ChunkSize = 16384;

    public Dictionary<string, byte[]> Contents { get; } = new Dictionary<string, byte[]>();

    /// <summary>Đặt giá trị này để ném CryptographicException sau đúng số byte đã ghi (giống phát hiện hỏng ở cuối tệp).</summary>
    public long? CorruptAfterBytes { get; set; }

    /// <summary>Mỗi lần CopyToAsync: đích có phải Stream.Null không, và số byte đã ghi.</summary>
    public List<(bool DestinationIsNull, long BytesWritten)> Calls { get; } = new List<(bool, long)>();

    public Task<StoredContent> SaveAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        throw new NotSupportedException();
    }

    public async Task CopyToAsync(StoredContent content, Stream destination, CancellationToken ct)
    {
        byte[] bytes = Contents[content.StorageKey];
        bool toNull = ReferenceEquals(destination, Stream.Null);
        long written = 0;

        try
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = Math.Min(ChunkSize, bytes.Length - offset);
                if (CorruptAfterBytes != null && written + count > CorruptAfterBytes.Value)
                {
                    int allowed = (int)(CorruptAfterBytes.Value - written);
                    if (allowed > 0)
                    {
                        await destination.WriteAsync(bytes.AsMemory(offset, allowed), ct);
                        written += allowed;
                    }
                    throw new System.Security.Cryptography.CryptographicException("Khối cuối bị hỏng (giả lập).");
                }

                await destination.WriteAsync(bytes.AsMemory(offset, count), ct);
                written += count;
                offset += count;
            }
        }
        finally
        {
            Calls.Add((toNull, written));
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        Contents.Remove(storageKey);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Unit of work giả luôn ném lỗi khi lưu, để thử nhánh xử lý lỗi của service.
/// Dùng DispatchProxy thay vì tự cài IUnitOfWork: khi nhánh khác thêm hàm mới vào IUnitOfWork
/// (ví dụ ExecuteSerializableAsync) test này vẫn biên dịch, không gây xung đột khi gộp.
/// </summary>
public class ThrowingUnitOfWork : System.Reflection.DispatchProxy
{
    private Exception failure = new InvalidOperationException();

    public static LegacyVault.Application.Repositories.IUnitOfWork Create(Exception failure)
    {
        LegacyVault.Application.Repositories.IUnitOfWork proxy =
            Create<LegacyVault.Application.Repositories.IUnitOfWork, ThrowingUnitOfWork>();
        ((ThrowingUnitOfWork)(object)proxy).failure = failure;
        return proxy;
    }

    protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod != null && targetMethod.Name == "SaveChangesAsync")
        {
            return Task.FromException(failure);
        }
        throw new NotSupportedException("Chỉ SaveChangesAsync được giả lập.");
    }
}
