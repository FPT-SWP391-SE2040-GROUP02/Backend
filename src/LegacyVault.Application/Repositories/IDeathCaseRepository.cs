using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Một kho mà tài khoản đang là Executor (DMS-05): dữ liệu thô để service dựng ExecutorVaultResponse.</summary>
public sealed class ExecutorVaultRow
{
    public Guid VaultId { get; set; }

    /// <summary>Họ tên tài khoản Owner (dùng để Executor nhận ra kho; không phải dữ liệu trong hồ sơ chứng tử).</summary>
    public string OwnerFullName { get; set; } = string.Empty;

    public ExecutorRole Role { get; set; }

    public PlanStatus PlanStatus { get; set; }

    /// <summary>Hồ sơ đang mở của kho (kể cả Draft); null khi chưa có.</summary>
    public Guid? OpenDeathCaseId { get; set; }
}

/// <summary>
/// Truy cập các bảng hồ sơ chứng tử: DeathCases, DeathCaseVersions, DeathCaseFiles, DeathCaseFlags,
/// DeathCaseNotices, DeathCaseAssignments.
/// </summary>
/// <remarks>
/// Repository chỉ đánh dấu Add/Remove/sửa; service gọi IUnitOfWork.SaveChangesAsync một lần ở cuối use case (STATE-02).
/// Riêng TryAddAsync tự lưu để bắt vi phạm "một hồ sơ mỗi kho" (giống ContainerRepository.TryAddAssetLinkAsync).
/// </remarks>
public interface IDeathCaseRepository
{
    /// <summary>
    /// Hồ sơ theo Id (có theo dõi), kèm Versions.Files, Flags, Notices và phân công CÒN HIỆU LỰC
    /// (Assignments chỉ chứa dòng EndedAt null). Không có → null.
    /// </summary>
    Task<DeathCase?> FindByIdAsync(Guid caseId, CancellationToken ct);

    /// <summary>DEATH-05: hồ sơ đang mở (chưa bị từ chối) của kho, cùng phần nạp kèm như FindByIdAsync. Không có → null.</summary>
    Task<DeathCase?> FindOpenByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// D12, DMS-05: các kho mà tài khoản là Executor đang hiệu lực (Status Accepted), chấp nhận sớm nhất trước.
    /// Chỉ đọc bảng Executors, không đổi IExecutorReader.
    /// </summary>
    Task<List<ExecutorVaultRow>> FindExecutorVaultsAsync(Guid userId, CancellationToken ct);

    /// <summary>Hồ sơ bị từ chối gần nhất của kho (không nạp kèm), để nối PreviousCaseId. Không có → null.</summary>
    Task<DeathCase?> FindLatestRejectedByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// DEATH-05, AC-71: thêm hồ sơ mới (kèm các phiên bản trong Versions) rồi lưu ngay cùng mọi thay đổi đang chờ.
    /// Trả false khi DB báo kho đã có hồ sơ đang mở (request khác vừa tạo trước); khi đó hồ sơ và phiên bản
    /// được bỏ theo dõi, người gọi nên DiscardPendingChanges rồi đọc lại hồ sơ bên thắng.
    /// </summary>
    Task<bool> TryAddAsync(DeathCase deathCase, CancellationToken ct);

    void AddVersion(DeathCaseVersion version);

    void AddFile(DeathCaseFile file);

    void RemoveFile(DeathCaseFile file);

    void AddFlag(DeathCaseFlag flag);

    void AddNotice(DeathCaseNotice notice);

    void AddAssignment(DeathCaseAssignment assignment);

    /// <summary>D8: số dòng file (mọi phiên bản, mọi hồ sơ) đang dùng StorageKey này, đã lưu trong DB.</summary>
    Task<int> CountFilesUsingStorageKeyAsync(string storageKey, CancellationToken ct);

    /// <summary>DEATH-10: kế hoạch có lần điểm danh nào lúc cutoffUtc hoặc sau đó không.</summary>
    Task<bool> HasCheckInAtOrAfterAsync(Guid planId, DateTimeOffset cutoffUtc, CancellationToken ct);

    /// <summary>
    /// ASSIGN-01, ROLE-03 (D4): Id Verifier Active có lần được giao gần nhất cũ nhất (chưa từng giao đứng trước),
    /// hòa thì theo Id; bỏ qua các tài khoản trong excludedUserIds và tài khoản có email nằm trong excludedEmails
    /// (ROLE-03: người nhận của kho, email đã chuẩn hóa chữ thường). Không ai phù hợp → null.
    /// </summary>
    Task<Guid?> FindVerifierCandidateAsync(List<Guid> excludedUserIds, List<string> excludedEmails, CancellationToken ct);

    /// <summary>D3: Id các thông báo Pending, tạo sớm nhất trước, tối đa maxCount dòng.</summary>
    Task<List<Guid>> FindPendingNoticeIdsAsync(int maxCount, CancellationToken ct);

    /// <summary>Thông báo theo Id (có theo dõi) để job xử lý. Không có → null.</summary>
    Task<DeathCaseNotice?> FindNoticeForJobAsync(Guid noticeId, CancellationToken ct);

    /// <summary>
    /// true nếu lỗi lưu là do vi phạm chỉ mục unique (ví dụ hai Executor gửi cùng lúc cùng tạo phân công hoặc cờ).
    /// Giúp Application nhận ra tình huống này mà không phải biết exception của EF/SQL Server.
    /// </summary>
    bool IsUniqueViolation(Exception exception);

    /// <summary>
    /// Bỏ MỌI thay đổi chưa lưu của cả request (ChangeTracker.Clear). Chỉ gọi khi use case sẽ trả kết quả
    /// (hoặc ném lỗi) mà KHÔNG SaveChanges nữa, hoặc trước khi đọc lại dữ liệu mới từ DB.
    /// </summary>
    void DiscardPendingChanges();
}
