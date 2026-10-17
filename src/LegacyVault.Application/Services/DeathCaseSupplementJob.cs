using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Việc nền ghi nhận Executor quá hạn bổ sung hồ sơ chứng tử (DEATH-01).</summary>
public interface IDeathCaseSupplementJob
{
    Task<List<Guid>> FindOverdueCaseIdsAsync(CancellationToken ct);

    /// <summary>Ghi nhận quá hạn một hồ sơ. Đọc lại và kiểm lại nên chạy lặp an toàn. Trả true khi vừa ghi.</summary>
    Task<bool> ProcessCaseAsync(Guid caseId, CancellationToken ct);
}

/// <remarks>
/// Quá hạn không tự quyết định gì: hồ sơ vẫn SUPPLEMENT_REQUIRED, chỉ có SupplementOverdueAt để Verifier thấy
/// và tự từ chối hoặc yêu cầu lại. Executor nộp bản mới thì mốc này được xóa (DeathCaseService.SubmitAsync).
/// Không đổi UpdatedAt của hồ sơ nên etag của Executor/Verifier không bị đổi.
/// </remarks>
public sealed class DeathCaseSupplementJob : IDeathCaseSupplementJob
{
    private const int BatchSize = 50;

    private readonly ISupplementOverdueRepository repository;
    private readonly IDeathCaseRepository deathCases;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public DeathCaseSupplementJob(
        ISupplementOverdueRepository repository,
        IDeathCaseRepository deathCases,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.repository = repository;
        this.deathCases = deathCases;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public Task<List<Guid>> FindOverdueCaseIdsAsync(CancellationToken ct)
    {
        return repository.FindOverdueCaseIdsAsync(timeProvider.GetUtcNow(), BatchSize, ct);
    }

    public async Task<bool> ProcessCaseAsync(Guid caseId, CancellationToken ct)
    {
        DeathCase? deathCase = await deathCases.FindByIdAsync(caseId, ct);
        if (deathCase == null || deathCase.Status != DeathCaseStatus.SupplementRequired || deathCase.SupplementOverdueAt != null)
        {
            return false;
        }

        DeathCaseDecision? request = null;
        foreach (DeathCaseDecision decision in deathCase.Decisions)
        {
            if (decision.Kind == DeathCaseDecisionKind.SupplementRequested
                && decision.VersionNo == deathCase.ReviewVersionNo
                && (request == null || decision.DecidedAt > request.DecidedAt))
            {
                request = decision;
            }
        }
        DateTimeOffset now = timeProvider.GetUtcNow();
        // TIME-02: đúng giây hạn là quá hạn.
        if (request == null || request.DueAt == null || now < request.DueAt.Value)
        {
            return false;
        }

        deathCase.SupplementOverdueAt = now;
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
