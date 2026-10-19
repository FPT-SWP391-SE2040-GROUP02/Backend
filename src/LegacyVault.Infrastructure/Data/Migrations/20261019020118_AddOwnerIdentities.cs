using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "EnrollmentIdentityEncrypted",
                table: "EkycVerifications",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnrollmentIdentityKeyId",
                table: "EkycVerifications",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "EnrollmentWrappedDataKey",
                table: "EkycVerifications",
                type: "binary(60)",
                fixedLength: true,
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OwnerIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CitizenIdLast4 = table.Column<string>(type: "char(4)", unicode: false, fixedLength: true, maxLength: 4, nullable: false),
                    SourceVerificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncryptionKeyId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    WrappedDataKey = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: false),
                    IdentityEncrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerIdentities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerIdentities_UserId",
                table: "OwnerIdentities",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerIdentities");

            migrationBuilder.DropColumn(
                name: "EnrollmentIdentityEncrypted",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "EnrollmentIdentityKeyId",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "EnrollmentWrappedDataKey",
                table: "EkycVerifications");
        }
    }
}
