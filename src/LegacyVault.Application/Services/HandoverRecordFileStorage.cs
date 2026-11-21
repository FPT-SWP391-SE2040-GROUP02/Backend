using System.Security.Cryptography;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Lưu, đọc và xóa tệp đính kèm biên bản (giấy tờ người nhận nộp, tệp căn cứ của luật sư) qua kho lưu trữ mã hóa.
/// </summary>
/// <remarks>
/// - Chỉ nhận PDF, JPEG, PNG, WebP theo chữ ký đầu tệp, không tin Content-Type của request.
/// - Tối đa 10 tệp mỗi lần, mỗi tệp tối đa 10 MiB.
/// - Không log nội dung, tên tệp hay khóa lưu trữ.
/// </remarks>
public static class HandoverRecordFileStorage
{
    public const long MaxFileBytes = 10 * 1024 * 1024;

    public const int MaxFilesPerUpload = 10;

    /// <summary>Kiểm số tệp, kích thước và loại tệp trước khi lưu; sai thì 400 với field "files".</summary>
    public static void Validate(List<UploadedFile> files, bool required)
    {
        if (files.Count == 0)
        {
            if (required)
            {
                throw ValidationErrors.For("files", "Vui lòng đính kèm ít nhất một tệp giấy tờ.");
            }
            return;
        }
        if (files.Count > MaxFilesPerUpload)
        {
            throw ValidationErrors.For("files", "Mỗi lần nộp tối đa 10 tệp.");
        }
        foreach (UploadedFile file in files)
        {
            if (file.Content.Length == 0)
            {
                throw ValidationErrors.For("files", "Có tệp rỗng.");
            }
            if (file.Content.Length > MaxFileBytes)
            {
                throw new AppException(AppErrorType.PayloadTooLarge, ErrorCode.FileTooLarge,
                    "Mỗi tệp tối đa 10 MiB.");
            }
            if (DetectContentType(file.Content) == null)
            {
                throw ValidationErrors.For("files", "Chỉ nhận tệp PDF hoặc ảnh JPG, PNG, WebP.");
            }
        }
    }

    /// <summary>Mã hóa và ghi các tệp; lỗi giữa chừng thì xóa các tệp đã ghi rồi ném lại.</summary>
    public static async Task<List<HandoverRecordFile>> SaveAllAsync(
        IAssetContentStore contentStore, List<UploadedFile> files, Guid recordId, Guid? claimId,
        HandoverRecordFileKind kind, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        List<HandoverRecordFile> saved = new List<HandoverRecordFile>();
        try
        {
            int offset = 0;
            foreach (UploadedFile file in files)
            {
                StoredContent stored;
                using (MemoryStream stream = new MemoryStream(file.Content, false))
                {
                    stored = await contentStore.SaveAsync(stream, MaxFileBytes, ct);
                }
                HandoverRecordFile entity = new HandoverRecordFile
                {
                    RecordId = recordId,
                    ClaimId = claimId,
                    Kind = kind,
                    FileName = FileSignatureRules.SanitizeFileName(file.FileName),
                    ContentType = DetectContentType(file.Content)!,
                    SizeBytes = stored.SizeBytes,
                    StorageKey = stored.StorageKey,
                    EncryptionKeyId = stored.EncryptionKeyId,
                    WrappedDataKey = stored.WrappedDataKey,
                    UploadedByUserId = userId,
                    // Cộng dần từng tích tắc để giữ đúng thứ tự tệp người dùng chọn.
                    CreatedAt = now.AddTicks(offset)
                };
                saved.Add(entity);
                offset = offset + 1;
            }
        }
        catch (Exception)
        {
            await DeleteAllAsync(contentStore, saved);
            throw;
        }
        return saved;
    }

    /// <summary>Bù trừ khi lưu DB thất bại, hoặc dọn sau khi xóa bản ghi. Lỗi thì bỏ qua (object mồ côi đã mã hóa).</summary>
    public static async Task DeleteAllAsync(IAssetContentStore contentStore, List<HandoverRecordFile> files)
    {
        foreach (HandoverRecordFile file in files)
        {
            await IdentityImageStorage.TryDeleteAsync(contentStore, file.StorageKey);
        }
    }

    /// <summary>Kiểm toàn vẹn rồi trả bản tải giải mã thẳng ra response. Hỏng → ContentIntegrityException.</summary>
    public static async Task<PreparedDownload> PrepareAsync(
        IAssetContentStore contentStore, HandoverRecordFile file, CancellationToken ct)
    {
        StoredContent stored = new StoredContent
        {
            StorageKey = file.StorageKey,
            SizeBytes = file.SizeBytes,
            EncryptionKeyId = file.EncryptionKeyId,
            WrappedDataKey = file.WrappedDataKey
        };
        try
        {
            await contentStore.CopyToAsync(stored, Stream.Null, ct);
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException)
        {
            throw new ContentIntegrityException("Tệp đính kèm không vượt qua kiểm tra toàn vẹn.", ex);
        }
        PreparedDownload download = new PreparedDownload
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            Length = file.SizeBytes,
            WriteToAsync = (destination, token) => contentStore.CopyToAsync(stored, destination, token)
        };
        return download;
    }

    private static string? DetectContentType(byte[] content)
    {
        int length = Math.Min(content.Length, FileSignatureRules.HeaderLength);
        byte[] header = new byte[FileSignatureRules.HeaderLength];
        Array.Copy(content, header, length);
        return FileSignatureRules.DetectContentType(header);
    }
}
