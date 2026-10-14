using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Mappings;

public static class DeathCaseMappings
{
    /// <summary>
    /// Cần hồ sơ đã nạp Versions.Files và Notices. identity là dữ liệu đã giải mã của phiên bản hiện tại
    /// (null khi chưa nhập gì). Không đưa CCCD đầy đủ vào response (DATA-02).
    /// </summary>
    public static DeathCaseResponse ToResponse(this DeathCase deathCase, DeathCaseIdentityData? identity)
    {
        DeathCaseVersion current = FindVersion(deathCase, deathCase.CurrentVersionNo);

        DeathCaseVersionResponse version = new DeathCaseVersionResponse();
        version.VersionNo = current.VersionNo;
        version.IsDraft = current.SubmittedAt == null;
        version.OwnerCitizenIdLast4 = current.OwnerCitizenIdLast4;
        version.DateOfDeath = current.DateOfDeath;
        version.IssuedOn = current.IssuedOn;
        version.SubmittedAt = current.SubmittedAt;

        string? ownerFullName = null;
        string? ownerCitizenId = null;
        string? certificateNumber = null;
        string? issuedBy = null;
        if (identity != null)
        {
            ownerFullName = identity.OwnerFullName;
            ownerCitizenId = identity.OwnerCitizenId;
            certificateNumber = identity.CertificateNumber;
            issuedBy = identity.IssuedBy;
        }
        version.OwnerFullName = ownerFullName;
        version.CertificateNumber = certificateNumber;
        version.IssuedBy = issuedBy;

        List<DeathCaseFile> orderedFiles = new List<DeathCaseFile>(current.Files);
        orderedFiles.Sort((left, right) => left.Position.CompareTo(right.Position));
        foreach (DeathCaseFile file in orderedFiles)
        {
            DeathCaseFileResponse fileResponse = new DeathCaseFileResponse();
            fileResponse.Id = file.Id;
            fileResponse.Position = file.Position;
            fileResponse.FileName = file.FileName;
            fileResponse.ContentType = file.ContentType;
            fileResponse.SizeBytes = file.SizeBytes;
            fileResponse.UploadedAt = file.UploadedAt;
            version.Files.Add(fileResponse);
        }

        DeathCaseResponse response = new DeathCaseResponse();
        response.Id = deathCase.Id;
        response.VaultId = deathCase.VaultId;
        response.Status = deathCase.Status.ToApiString();
        response.CurrentVersion = version;
        response.ReviewVersionNo = deathCase.ReviewVersionNo;
        response.MissingRequirements = DeathCaseRules.GetMissingRequirements(
            ownerFullName, ownerCitizenId, current.DateOfDeath, certificateNumber, current.IssuedOn, issuedBy, current.Files.Count);
        response.OwnerNotice = ToNoticeResponse(deathCase);
        response.ObjectionEndsAt = deathCase.ObjectionEndsAt;
        response.CreatedAt = deathCase.CreatedAt;
        response.UpdatedAt = deathCase.UpdatedAt;
        response.Etag = Etag.From(deathCase.UpdatedAt);
        return response;
    }

    public static ExecutorVaultResponse ToResponse(this ExecutorVaultRow row)
    {
        ExecutorVaultResponse response = new ExecutorVaultResponse();
        response.VaultId = row.VaultId;
        response.OwnerFullName = row.OwnerFullName;
        response.Role = row.Role.ToApiString();
        response.PlanStatus = row.PlanStatus.ToApiString();
        // DMS-05: quá hạn điểm danh = đang trong thời gian chờ hoặc đã hết thời gian chờ.
        response.CheckInOverdue = row.PlanStatus == PlanStatus.CheckinOverdue || row.PlanStatus == PlanStatus.AwaitingCheck;
        response.OpenDeathCaseId = row.OpenDeathCaseId;
        return response;
    }

    /// <summary>Phiên bản theo số thứ tự. Dữ liệu hợp lệ luôn có bản CurrentVersionNo.</summary>
    public static DeathCaseVersion FindVersion(DeathCase deathCase, int versionNo)
    {
        foreach (DeathCaseVersion version in deathCase.Versions)
        {
            if (version.VersionNo == versionNo)
            {
                return version;
            }
        }
        throw new InvalidOperationException("Hồ sơ chứng tử thiếu phiên bản hiện tại.");
    }

    /// <summary>Thông báo của bản mới nhất; cùng bản thì lấy thông báo tạo sau. Chưa có → null.</summary>
    private static DeathCaseNoticeResponse? ToNoticeResponse(DeathCase deathCase)
    {
        DeathCaseNotice? latest = null;
        foreach (DeathCaseNotice notice in deathCase.Notices)
        {
            if (latest == null
                || notice.VersionNo > latest.VersionNo
                || (notice.VersionNo == latest.VersionNo && notice.CreatedAt > latest.CreatedAt))
            {
                latest = notice;
            }
        }
        if (latest == null)
        {
            return null;
        }

        DeathCaseNoticeResponse response = new DeathCaseNoticeResponse();
        response.Status = latest.Status.ToApiString();
        response.DeliveredAt = latest.DeliveredAt;
        return response;
    }
}
