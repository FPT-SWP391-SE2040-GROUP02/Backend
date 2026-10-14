using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoverReceive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HandoverAccessLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverAccessLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HandoverTurns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DesignationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BeneficiaryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InvitationTokenHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    InvitedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResponseDeadline = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InvitationSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimantUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IdentityBypassed = table.Column<bool>(type: "bit", nullable: false),
                    ReadyAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionDeadline = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverTurns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HandoverGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverGrants_HandoverTurns_TurnId",
                        column: x => x.TurnId,
                        principalTable: "HandoverTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HandoverSnapshotItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverSnapshotItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverSnapshotItems_HandoverTurns_TurnId",
                        column: x => x.TurnId,
                        principalTable: "HandoverTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverAccessLogs_GrantId_At",
                table: "HandoverAccessLogs",
                columns: new[] { "GrantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverGrants_Status_ExpiresAt",
                table: "HandoverGrants",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverGrants_TurnId",
                table: "HandoverGrants",
                column: "TurnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HandoverGrants_UserId",
                table: "HandoverGrants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverSnapshotItems_TurnId_Position",
                table: "HandoverSnapshotItems",
                columns: new[] { "TurnId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverTurns_ClaimantUserId_Status",
                table: "HandoverTurns",
                columns: new[] { "ClaimantUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverTurns_InvitationTokenHash",
                table: "HandoverTurns",
                column: "InvitationTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HandoverTurns_PlanId",
                table: "HandoverTurns",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverTurns_Status_DecisionDeadline",
                table: "HandoverTurns",
                columns: new[] { "Status", "DecisionDeadline" });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverTurns_Status_ResponseDeadline",
                table: "HandoverTurns",
                columns: new[] { "Status", "ResponseDeadline" });

            migrationBuilder.CreateIndex(
                name: "UX_HandoverTurns_ActivePerDesignation",
                table: "HandoverTurns",
                column: "DesignationId",
                unique: true,
                filter: "[Status] IN ('Invited','InitialResponseExpired','IdentityInProgress','ReadyToAccept','HandoverCommitted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HandoverAccessLogs");

            migrationBuilder.DropTable(
                name: "HandoverGrants");

            migrationBuilder.DropTable(
                name: "HandoverSnapshotItems");

            migrationBuilder.DropTable(
                name: "HandoverTurns");
        }
    }
}
