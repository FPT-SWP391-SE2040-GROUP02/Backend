using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEkycLeftovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dọn hàng cũ trước khi bỏ giá trị enum: EF đọc Purpose/Type dạng chuỗi, gặp giá trị không còn trong enum sẽ lỗi.
            // eKYC chỉ còn mục đích OwnerEnrollment; lượt và lần đồng ý của người nhận (BeneficiaryClaim) hay báo còn sống
            // (OwnerAlive) bị xóa. Hold loại OwnerAlive không có mã nào tạo nên không dự kiến có hàng, vẫn xóa cho chắc.
            migrationBuilder.Sql("DELETE FROM [EkycConsents] WHERE [Purpose] <> 'OwnerEnrollment';");
            migrationBuilder.Sql("DELETE FROM [EkycVerifications] WHERE [Purpose] <> 'OwnerEnrollment';");
            migrationBuilder.Sql("DELETE FROM [Holds] WHERE [Type] = 'OwnerAlive';");

            migrationBuilder.DropTable(
                name: "OwnerFaceTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Holds_VaultId_Type_Scope_TargetId",
                table: "Holds");

            migrationBuilder.DropColumn(
                name: "HandoverTurnId",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "IdentityMatch",
                table: "EkycVerifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Hoàn nguyên chỉ tạo lại cấu trúc; dữ liệu đã xóa ở Up (mẫu khuôn mặt, lượt eKYC người nhận) không khôi phục được.
            migrationBuilder.AddColumn<Guid>(
                name: "HandoverTurnId",
                table: "EkycVerifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityMatch",
                table: "EkycVerifications",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "OwnerFaceTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Dimension = table.Column<int>(type: "int", nullable: false),
                    EmbeddingEncrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    EncryptionKeyId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ModelName = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    SourceVerificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    WrappedDataKey = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerFaceTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerFaceTemplates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holds_VaultId_Type_Scope_TargetId",
                table: "Holds",
                columns: new[] { "VaultId", "Type", "Scope", "TargetId" },
                unique: true,
                filter: "[Status] = 'Active' AND [Type] = 'OwnerAlive'");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFaceTemplates_UserId_Version",
                table: "OwnerFaceTemplates",
                columns: new[] { "UserId", "Version" },
                unique: true);
        }
    }
}
