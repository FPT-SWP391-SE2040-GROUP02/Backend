using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using System.Security.Cryptography;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Security;
using LegacyVault.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace LegacyVault.IntegrationTests.Ekyc;

// Các class giả để test eKYC mà không cần SQL Server hay dịch vụ Python.

public sealed class FakeEkycVerificationRepository : IEkycVerificationRepository
{
    public List<EkycVerification> Verifications { get; } = new List<EkycVerification>();

    public Task<EkycVerification?> FindLatestByUserIdAsync(Guid userId, CancellationToken ct)
    {
        EkycVerification? latest = Verifications
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    public Task<EkycVerification?> FindLatestByUserAndPurposeAsync(Guid userId, EkycPurpose purpose, CancellationToken ct)
    {
        EkycVerification? latest = Verifications
            .Where(v => v.UserId == userId && v.Purpose == purpose)
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    public Task<EkycVerification?> FindLatestPassedByUserAndPurposeAsync(Guid userId, EkycPurpose purpose, CancellationToken ct)
    {
        EkycVerification? latest = Verifications
            .Where(v => v.UserId == userId && v.Purpose == purpose && v.Status == EkycStatus.Passed)
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    public Task<EkycVerification?> FindLatestByHandoverTurnAsync(Guid userId, Guid handoverTurnId, CancellationToken ct)
    {
        EkycVerification? latest = Verifications
            .Where(v => v.UserId == userId && v.HandoverTurnId == handoverTurnId)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    public void Add(EkycVerification verification)
    {
        Verifications.Add(verification);
    }
}

public sealed class FakeEkycConsentRepository : IEkycConsentRepository
{
    public List<EkycConsent> Consents { get; } = new List<EkycConsent>();

    public Task<EkycConsent?> FindLatestByUserIdAsync(Guid userId, CancellationToken ct)
    {
        EkycConsent? latest = Consents
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.ConsentedAt)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    public void Add(EkycConsent consent)
    {
        Consents.Add(consent);
    }
}

public sealed class FakeOwnerFaceTemplateRepository : IOwnerFaceTemplateRepository
{
    public List<OwnerFaceTemplate> Templates { get; } = new List<OwnerFaceTemplate>();

    public Task<OwnerFaceTemplate?> FindLatestByUserIdAsync(Guid userId, CancellationToken ct)
    {
        OwnerFaceTemplate? latest = Templates
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Version)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    public void Add(OwnerFaceTemplate template)
    {
        Templates.Add(template);
    }
}

public sealed class FakeOwnerIdentityRepository : IOwnerIdentityRepository
{
    public List<OwnerIdentity> Identities { get; } = new List<OwnerIdentity>();

    public Task<OwnerIdentity?> FindByUserIdAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(Identities.FirstOrDefault(i => i.UserId == userId));
    }

    public Task<List<OwnerIdentity>> ListByCitizenIdLast4ExceptUserAsync(string last4, Guid excludeUserId, CancellationToken ct)
    {
        return Task.FromResult(Identities
            .Where(i => i.CitizenIdLast4 == last4 && i.UserId != excludeUserId)
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .ToList());
    }

    public void Add(OwnerIdentity identity)
    {
        Identities.Add(identity);
    }
}

/// <summary>Bộ mã hóa mẫu khuôn mặt thật (khóa ngẫu nhiên) để test kiểm cả vòng mã hóa lẫn giải mã.</summary>
public static class FaceTemplateTestSupport
{
    public static FaceTemplateProtector CreateProtector()
    {
        AssetEncryptionSettings settings = new AssetEncryptionSettings();
        settings.ActiveKeyId = "test-key";
        settings.Keys["test-key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new FaceTemplateProtector(new DataKeyProtector(Options.Create(settings)));
    }

    public static OwnerIdentityProtector CreateIdentityProtector()
    {
        AssetEncryptionSettings settings = new AssetEncryptionSettings();
        settings.ActiveKeyId = "test-key";
        settings.Keys["test-key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new OwnerIdentityProtector(new DataKeyProtector(Options.Create(settings)));
    }

    /// <summary>Vector 128 số xác định theo seed; hai seed khác nhau cho hai vector gần như trực giao.</summary>
    public static float[] Vector(int seed)
    {
        Random random = new Random(seed);
        float[] values = new float[EkycRules.FaceEmbeddingLength];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = (float)(random.NextDouble() * 2 - 1);
        }
        return values;
    }
}

public sealed class FakeEkycBeneficiaryRepository : IBeneficiaryRepository
{
    public List<Beneficiary> Beneficiaries { get; } = new List<Beneficiary>();

    public Task<Beneficiary?> FindByIdAsync(Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        Beneficiary? found = Beneficiaries.FirstOrDefault(b => b.VaultId == vaultId && b.Id == beneficiaryId);
        return Task.FromResult(found);
    }

    public Task<List<Beneficiary>> ListByCitizenIdLast4Async(Guid vaultId, string last4, CancellationToken ct)
    {
        return Task.FromResult(new List<Beneficiary>());
    }

    public Task<List<Beneficiary>> ListByOwnerAndCitizenIdLast4Async(Guid ownerId, string last4, CancellationToken ct)
    {
        List<Beneficiary> found = Beneficiaries.Where(b => b.CitizenIdLast4 == last4).ToList();
        return Task.FromResult(found);
    }

    public Task<(List<Beneficiary> Items, int TotalCount)> FindPageByVaultIdAsync(
        Guid vaultId, string? last4Filter, int pageIndex, int pageSize, bool isAscending, CancellationToken ct)
    {
        return Task.FromResult((new List<Beneficiary>(), 0));
    }

    public Task<int> CountByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        int count = 0;
        foreach (Beneficiary beneficiary in Beneficiaries)
        {
            if (beneficiary.VaultId == vaultId)
            {
                count++;
            }
        }
        return Task.FromResult(count);
    }

    public void Add(Beneficiary beneficiary)
    {
        Beneficiaries.Add(beneficiary);
    }

    public void Remove(Beneficiary beneficiary)
    {
        Beneficiaries.Remove(beneficiary);
    }

    public bool IsUniqueViolation(Exception exception)
    {
        return false;
    }

    public void DiscardPendingChanges()
    {
    }
}

public sealed class FakeEkycClient : IEkycClient
{
    public EkycLivenessState LivenessStart { get; set; } = new EkycLivenessState
    {
        SessionId = "live-1",
        ExpiresInSeconds = 90,
        Prompt = "Nhìn thẳng, mở mắt và giữ yên khoảng 2 giây để bắt đầu",
        Actions = new List<EkycLivenessAction>
        {
            new EkycLivenessAction { Key = "blink", Label = "Nhắm mắt khoảng 1 giây, rồi mở mắt" },
            new EkycLivenessAction { Key = "left", Label = "Quay đầu nhẹ sang trái, giữ 1 giây, rồi nhìn thẳng" },
        },
    };

    public EkycLivenessState LivenessFrame { get; set; } = new EkycLivenessState { Prompt = "Nhắm mắt khoảng 1 giây, rồi mở mắt" };

    public byte[] LivenessSelfie { get; set; } = new byte[] { 9, 9, 9 };

    /// <summary>Đặt để mô phỏng dịch vụ từ chối lấy selfie (phiên chưa đạt, hết hạn...).</summary>
    public AppException? LivenessSelfieError { get; set; }

    public List<string> DeletedLivenessSessions { get; } = new List<string>();

    public List<string> FrameSessions { get; } = new List<string>();

    public int LivenessStartCount { get; private set; }

    public string? LastSelfieSession { get; private set; }

    public Task<EkycLivenessState> StartLivenessAsync(CancellationToken ct)
    {
        LivenessStartCount++;
        EkycLivenessState state = LivenessStart;
        if (LivenessStartCount > 1)
        {
            state = new EkycLivenessState
            {
                SessionId = "live-" + LivenessStartCount,
                ExpiresInSeconds = LivenessStart.ExpiresInSeconds,
                Actions = LivenessStart.Actions,
                Prompt = LivenessStart.Prompt,
            };
        }
        return Task.FromResult(state);
    }

    public Task<EkycLivenessState> SendLivenessFrameAsync(
        string sessionId, byte[] frame, string fileName, string contentType, CancellationToken ct)
    {
        FrameSessions.Add(sessionId);
        return Task.FromResult(LivenessFrame);
    }

    public Task<byte[]> GetLivenessSelfieAsync(string sessionId, CancellationToken ct)
    {
        LastSelfieSession = sessionId;
        if (LivenessSelfieError != null)
        {
            throw LivenessSelfieError;
        }
        return Task.FromResult(LivenessSelfie);
    }

    public Task DeleteLivenessAsync(string sessionId, CancellationToken ct)
    {
        DeletedLivenessSessions.Add(sessionId);
        return Task.CompletedTask;
    }

    public EkycDocumentReading Reading { get; set; } = new EkycDocumentReading
    {
        IdNumber = "079200012345",
        FullName = "NGUYEN VAN AN",
        DateOfBirth = "01/02/2000",
        DocumentToken = "token-1",
    };

    public EkycBackReading BackReading { get; set; } = new EkycBackReading { IdNumber = "079200012345" };

    public EkycSpoofCheck Spoof { get; set; } = new EkycSpoofCheck { Passed = true, LiveScore = 0.99 };

    public EkycFaceComparison Comparison { get; set; } = new EkycFaceComparison { Score = 0.6, Matched = true };

    public List<string> DeletedTokens { get; } = new List<string>();

    public EkycFaceEmbedding Embedding { get; set; } = new EkycFaceEmbedding
    {
        ModelName = "sface",
        Values = FaceTemplateTestSupport.Vector(1),
    };

    /// <summary>Đặt để mô phỏng dịch vụ không trả được vector (503, ảnh không có đúng một mặt...).</summary>
    public AppException? EmbeddingError { get; set; }

    public int EmbeddingCalls { get; private set; }

    public double? LastThreshold { get; private set; }

    public Task<EkycDocumentReading> ReadFrontAsync(byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        return Task.FromResult(Reading);
    }

    public Task<EkycBackReading> ReadBackAsync(byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        return Task.FromResult(BackReading);
    }

    public Task<EkycSpoofCheck> CheckSpoofAsync(byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        return Task.FromResult(Spoof);
    }

    public Task<EkycFaceComparison> CompareFaceAsync(
        string documentToken, byte[] selfie, string fileName, string contentType, double threshold, CancellationToken ct)
    {
        LastThreshold = threshold;
        return Task.FromResult(Comparison);
    }

    public Task DeleteDocumentAsync(string documentToken, CancellationToken ct)
    {
        DeletedTokens.Add(documentToken);
        return Task.CompletedTask;
    }

    public Task<EkycFaceEmbedding> GetFaceEmbeddingAsync(byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        EmbeddingCalls++;
        if (EmbeddingError != null)
        {
            throw EmbeddingError;
        }
        return Task.FromResult(Embedding);
    }
}
