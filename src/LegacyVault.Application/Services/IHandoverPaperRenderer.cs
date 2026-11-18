namespace LegacyVault.Application.Services;

/// <summary>Dựng ảnh PNG một trang của biên bản đồng ý nhận hoặc văn bản từ chối nhận di sản; cài đặt cụ thể nằm ở Infrastructure.</summary>
public interface IHandoverPaperRenderer
{
    byte[] Render(HandoverPaperData data);
}
