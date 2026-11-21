using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;

namespace LegacyVault.Api.Handover;

/// <summary>Đọc các tệp multipart vào bộ nhớ để chuyển xuống Application; tệp quá lớn thì 413.</summary>
public static class UploadedFileReader
{
    /// <summary>10 tệp, mỗi tệp tối đa 10 MiB, cộng phần đầu multipart.</summary>
    public const long MaxRequestBytes = 105 * 1024 * 1024;

    public static async Task<List<UploadedFile>> ReadAllAsync(List<IFormFile>? files, CancellationToken ct)
    {
        List<UploadedFile> result = new List<UploadedFile>();
        if (files == null)
        {
            return result;
        }
        foreach (IFormFile file in files)
        {
            if (file.Length > HandoverRecordFileStorage.MaxFileBytes)
            {
                throw new AppException(AppErrorType.PayloadTooLarge, ErrorCode.FileTooLarge, "Mỗi tệp tối đa 10 MiB.");
            }
            await using (Stream content = file.OpenReadStream())
            using (MemoryStream buffer = new MemoryStream())
            {
                await content.CopyToAsync(buffer, ct);
                UploadedFile item = new UploadedFile
                {
                    FileName = file.FileName,
                    Content = buffer.ToArray()
                };
                result.Add(item);
            }
        }
        return result;
    }
}
