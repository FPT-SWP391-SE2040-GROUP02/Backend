using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Mẫu biên bản đồng ý và văn bản từ chối: Admin xem, sửa và xem lịch sử; các service biên bản lấy mẫu theo phiên bản.
/// Chưa có bản nào được lưu thì dùng mẫu gốc (<see cref="HandoverTemplateDefaults"/>), khóa phiên bản là HandoverDocumentRules.TemplateVersion.
/// </summary>
public interface IHandoverTemplateService
{
    /// <summary>Mẫu đang dùng cho biên bản mới, kèm danh sách trường và mẫu gốc. Không phải Admin đang hoạt động → 404.</summary>
    Task<AdminHandoverTemplateResponse> GetCurrentAsync(CancellationToken ct);

    /// <summary>Một phiên bản cũ theo số. Không có → 404.</summary>
    Task<AdminHandoverTemplateResponse> GetVersionAsync(int version, CancellationToken ct);

    /// <summary>Lịch sử các lần lưu, mới nhất trước (tối đa 50).</summary>
    Task<List<HandoverTemplateVersionResponse>> ListVersionsAsync(CancellationToken ct);

    /// <summary>Lưu một phiên bản mới và ghi nhật ký. Sai luật → 400 VALIDATION_ERROR. Biên bản đã tạo giữ nguyên phiên bản cũ.</summary>
    Task<AdminHandoverTemplateResponse> SaveAsync(SaveHandoverTemplateRequest request, CancellationToken ct);

    /// <summary>Khóa phiên bản đang dùng cho biên bản mới: "set-N" hoặc mã mẫu gốc.</summary>
    Task<string> ActiveVersionKeyAsync(CancellationToken ct);

    /// <summary>Mẫu theo khóa đã ghi trên biên bản; khóa lạ hoặc không còn → mẫu gốc.</summary>
    Task<ResolvedHandoverTemplate> ResolveAsync(string versionKey, CancellationToken ct);
}
