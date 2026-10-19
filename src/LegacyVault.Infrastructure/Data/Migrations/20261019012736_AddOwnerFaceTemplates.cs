using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerFaceTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OwnerFaceTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ModelName = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Dimension = table.Column<int>(type: "int", nullable: false),
                    SourceVerificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncryptionKeyId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    WrappedDataKey = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: false),
                    EmbeddingEncrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                name: "IX_OwnerFaceTemplates_UserId_Version",
                table: "OwnerFaceTemplates",
                columns: new[] { "UserId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerFaceTemplates");
        }
    }
}
