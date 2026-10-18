using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "HandoverPlans",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "Executors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InviteeEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InvitationTokenHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    IsBeneficiary = table.Column<bool>(type: "bit", nullable: false),
                    BeneficiaryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvitedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReminderSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Executors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Executors_Beneficiaries_BeneficiaryId",
                        column: x => x.BeneficiaryId,
                        principalTable: "Beneficiaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Executors_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Executors_Vaults_VaultId",
                        column: x => x.VaultId,
                        principalTable: "Vaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Executors_BeneficiaryId",
                table: "Executors",
                column: "BeneficiaryId");

            migrationBuilder.CreateIndex(
                name: "IX_Executors_InvitationTokenHash",
                table: "Executors",
                column: "InvitationTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Executors_UserId",
                table: "Executors",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Executors_VaultId_Status",
                table: "Executors",
                columns: new[] { "VaultId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Executors");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "HandoverPlans");
        }
    }
}
