using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;

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

    public void Add(EkycVerification verification)
    {
        Verifications.Add(verification);
    }
}

public sealed class FakeEkycClient : IEkycClient
{
    public EkycDocumentReading Reading { get; set; } = new EkycDocumentReading
    {
        IdNumber = "079200012345",
        FullName = "NGUYEN VAN AN",
        DateOfBirth = "01/02/2000",
        DocumentToken = "token-1",
    };

    public EkycSpoofCheck Spoof { get; set; } = new EkycSpoofCheck { Passed = true, LiveScore = 0.99 };

    public EkycFaceComparison Comparison { get; set; } = new EkycFaceComparison { Score = 0.6, Matched = true };

    public List<string> DeletedTokens { get; } = new List<string>();

    public double? LastThreshold { get; private set; }

    public Task<EkycDocumentReading> ReadFrontAsync(byte[] image, string fileName, string contentType, CancellationToken ct)
    {
        return Task.FromResult(Reading);
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
}
