// Nạp các thực thể miền và thư viện Fluent API của Entity Framework Core để cấu hình cơ sở dữ liệu.
using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

// Không gian tên chứa các lớp cấu hình ánh xạ bảng cơ sở dữ liệu cho thực thể.
namespace LegacyVault.Infrastructure.Data.Configurations;

// Lớp cấu hình ánh xạ thực thể gói bàn giao vào bảng Containers trong cơ sở dữ liệu.
public sealed class ContainerConfiguration : IEntityTypeConfiguration<Container>
{
    // Phương thức thiết lập chi tiết các thuộc tính, khóa chính, độ dài cột và quan hệ giữa các bảng.
    public void Configure(EntityTypeBuilder<Container> builder)
    {
        // Ánh xạ thực thể với bảng có tên Containers trong cơ sở dữ liệu SQL Server.
        builder.ToTable("Containers");

        // Thiết lập trường Id làm khóa chính định danh cho bảng gói bàn giao.
        builder.HasKey(c => c.Id);

        // Cấu hình thuộc tính tên gói là trường bắt buộc và giới hạn độ dài tối đa 200 ký tự.
        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        // Khớp CreateContainerRequest/UpdateContainerRequest (tối đa 1000 ký tự).
        // Cấu hình thuộc tính mô tả gói có độ dài tối đa là 1000 ký tự và cho phép mang giá trị null.
        builder.Property(c => c.Description)
            .HasMaxLength(1000);

        // Cấu hình thuộc tính trạng thái gói là bắt buộc, lưu dưới dạng chuỗi tối đa 32 ký tự thay vì số nguyên.
        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // ETag của gói (UpdateContainerRequest.Etag), giống Vault.
        // Đánh dấu mốc thời gian cập nhật làm token chống ghi đè đồng thời khi sửa đổi dữ liệu.
        builder.Property(c => c.UpdatedAt)
            .IsConcurrencyToken();

        // Tạo chỉ mục kết hợp trên mã kho và trạng thái để tối ưu tốc độ tìm kiếm và đếm số gói theo kho.
        builder.HasIndex(c => new { c.VaultId, c.Status });

        // Không cascade từ kho: dữ liệu kho không bị xóa dây chuyền.
        // Quan hệ Container → ContainerAssets khai báo ở ContainerAssetConfiguration.
        // Cấu hình quan hệ một-nhiều: một kho có nhiều gói, sử dụng khóa ngoại VaultId và chặn xóa lan truyền.
        builder.HasOne(c => c.Vault)
            .WithMany(v => v.Containers)
            .HasForeignKey(c => c.VaultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
