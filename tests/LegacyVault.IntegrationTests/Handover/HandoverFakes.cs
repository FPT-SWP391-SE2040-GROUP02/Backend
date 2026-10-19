using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;

namespace LegacyVault.IntegrationTests.Handover;

// Các class giả để test giai đoạn nhận tài sản mà không cần SQL Server. Dữ liệu chỉ nằm trong List.

public sealed class FakeHandoverRepository : IHandoverRepository
{
    public List<HandoverTurn> Turns { get; } = new List<HandoverTurn>();
    public List<HandoverGrant> Grants { get; } = new List<HandoverGrant>();
    public List<HandoverAccessLog> Logs { get; } = new List<HandoverAccessLog>();
    public List<Designation> Designations { get; } = new List<Designation>();
    public List<AssetVersion> Versions { get; } = new List<AssetVersion>();

    public Task<HandoverTurn?> FindTurnByTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        return Task.FromResult(Turns.FirstOrDefault(t => t.InvitationTokenHash == tokenHash));
    }

    public Task<HandoverTurn?> FindTurnByIdAsync(Guid turnId, CancellationToken ct)
    {
        return Task.FromResult(Turns.FirstOrDefault(t => t.Id == turnId));
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

    public Task<List<HandoverTurn>> FindTurnsByPlanAsync(Guid planId, CancellationToken ct)
    {
        return Task.FromResult(Turns.Where(t => t.PlanId == planId).ToList());
    }

    public Task<HandoverGrant?> FindGrantByIdAsync(Guid grantId, CancellationToken ct)
    {
        return Task.FromResult(Grants.FirstOrDefault(g => g.Id == grantId));
    }

    public Task<List<Designation>> FindStartableDesignationsAsync(Guid vaultId, CancellationToken ct)
    {
        List<Designation> found = Designations
            .Where(d => d.Container.VaultId == vaultId && d.Container.Status == ContainerStatus.Ready)
            .ToList();
        return Task.FromResult(found);
    }

    public Task<AssetVersion?> FindAssetVersionAsync(Guid assetVersionId, CancellationToken ct)
    {
        return Task.FromResult(Versions.FirstOrDefault(v => v.Id == assetVersionId));
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

    public void Protect(Beneficiary beneficiary, BeneficiaryPersonalData data)
    {
    }

    public BeneficiaryPersonalData Unprotect(Beneficiary beneficiary)
    {
        return Data[beneficiary.Id];
    }
}

/// <summary>Đọc hold giả: bật Active để mô phỏng kho đang bị giữ.</summary>
public sealed class FakeHoldReader : IHoldReader
{
    public bool Active { get; set; }

    public Task<bool> HasActiveHoldAsync(Guid vaultId, CancellationToken ct)
    {
        return Task.FromResult(Active);
    }
}

public sealed class FakePolicy : IHandoverPolicy
{
    public bool RequireIdentityVerification { get; set; } = true;
}

/// <summary>Dựng kho, kế hoạch, gói, tài sản, chỉ định và lượt cho test.</summary>
public sealed class HandoverTestData
{
    public HandoverTestData(DateTimeOffset now)
    {
        Now = now;
        Owner = new User { Email = "owner@example.com", FullName = "Chủ Kho", Status = UserStatus.Active, EmailVerifiedAt = now.AddDays(-100), Role = UserRole.User };
        Vault = new Vault { OwnerId = Owner.Id, Owner = Owner, Name = "Kho", Status = VaultStatus.Open, CreatedAt = now.AddDays(-100) };
        Vault.Plan = new HandoverPlan { VaultId = Vault.Id, Vault = Vault, Status = PlanStatus.DeathCaseOpen, CreatedAt = now.AddDays(-100) };
    }

    public DateTimeOffset Now { get; }
    public User Owner { get; }
    public Vault Vault { get; }
    public HandoverPlan Plan
    {
        get { return Vault.Plan; }
    }

    public List<Designation> Designations { get; } = new List<Designation>();
    public List<AssetVersion> Versions { get; } = new List<AssetVersion>();
    public Dictionary<Guid, BeneficiaryPersonalData> People { get; } = new Dictionary<Guid, BeneficiaryPersonalData>();

    /// <summary>Thêm một gói có sẵn số tài sản chỉ định (mỗi tài sản một phiên bản hiện hành).</summary>
    public Container AddContainer(string name, ContainerStatus status, int assetCount)
    {
        Container container = new Container { VaultId = Vault.Id, Vault = Vault, Name = name, Status = status, CreatedAt = Now.AddDays(-50) };
        for (int i = 0; i < assetCount; i++)
        {
            Asset asset = new Asset
            {
                VaultId = Vault.Id,
                Vault = Vault,
                Kind = AssetKind.File,
                Name = name + "-tai-san-" + (i + 1),
                CurrentVersionNumber = 1,
                CreatedAt = Now.AddDays(-49)
            };
            AssetVersion version = new AssetVersion
            {
                AssetId = asset.Id,
                Asset = asset,
                VersionNumber = 1,
                FileName = "tep-" + (i + 1) + ".bin",
                ContentType = "application/pdf",
                SizeBytes = 1000 + i,
                Sha256 = new string('a', 64),
                StorageKey = "key-" + Guid.NewGuid().ToString("N"),
                EncryptionKeyId = "k1",
                CreatedAt = Now.AddDays(-49)
            };
            asset.Versions.Add(version);
            Versions.Add(version);
            container.Assets.Add(new ContainerAsset
            {
                ContainerId = container.Id,
                Container = container,
                AssetId = asset.Id,
                Asset = asset,
                AddedAt = Now.AddDays(-48).AddMinutes(i)
            });
        }
        Vault.Containers.Add(container);
        return container;
    }

    /// <summary>Chỉ định một hồ sơ người nhận mới vào gói.</summary>
    public Designation Designate(Container container, string fullName, string? email)
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

        Designation designation = new Designation
        {
            ContainerId = container.Id,
            Container = container,
            BeneficiaryId = beneficiary.Id,
            Beneficiary = beneficiary,
            CreatedAt = Now.AddDays(-30)
        };
        container.Designations.Add(designation);
        Designations.Add(designation);
        return designation;
    }

    /// <summary>Dựng lượt ở trạng thái cho trước (kế hoạch được đặt HandoverInProgress) với token cho trước.</summary>
    public HandoverTurn BuildTurn(Container container, Designation designation, HandoverTurnStatus status, string token, Guid? claimant)
    {
        HandoverTurn turn = new HandoverTurn
        {
            VaultId = Vault.Id,
            PlanId = Plan.Id,
            ContainerId = container.Id,
            ContainerName = container.Name,
            DesignationId = designation.Id,
            BeneficiaryId = designation.BeneficiaryId,
            Status = status,
            InvitationTokenHash = new FakeTokenService().HashOpaqueToken(token),
            InvitedAt = Now,
            ResponseDeadline = Now.AddDays(5),
            ClaimantUserId = claimant,
            CreatedAt = Now
        };

        int position = 0;
        foreach (ContainerAsset link in container.Assets)
        {
            AssetVersion version = link.Asset.Versions[0];
            turn.Items.Add(new HandoverSnapshotItem
            {
                TurnId = turn.Id,
                Turn = turn,
                AssetId = link.AssetId,
                AssetVersionId = version.Id,
                Name = link.Asset.Name,
                Kind = link.Asset.Kind,
                FileName = version.FileName,
                ContentType = version.ContentType,
                SizeBytes = version.SizeBytes,
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
