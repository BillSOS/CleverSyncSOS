using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleverSyncSOS.Core.Database.SessionDb.Migrations
{
    /// <inheritdoc />
    public partial class AddEventsSummaryJson_SessionDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventsSummaryJson",
                table: "SyncHistory",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SyncWarnings",
                columns: table => new
                {
                    SyncWarningId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SyncId = table.Column<int>(type: "int", nullable: false),
                    WarningType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    CleverEntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AffectedWorkshops = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AffectedWorkshopCount = table.Column<int>(type: "int", nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncWarnings", x => x.SyncWarningId);
                    table.ForeignKey(
                        name: "FK_SyncWarnings_SyncHistory_SyncId",
                        column: x => x.SyncId,
                        principalTable: "SyncHistory",
                        principalColumn: "SyncId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncWarnings_CreatedAt",
                table: "SyncWarnings",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SyncWarnings_IsAcknowledged",
                table: "SyncWarnings",
                column: "IsAcknowledged");

            migrationBuilder.CreateIndex(
                name: "IX_SyncWarnings_SyncId",
                table: "SyncWarnings",
                column: "SyncId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncWarnings_WarningType",
                table: "SyncWarnings",
                column: "WarningType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncWarnings");

            migrationBuilder.DropColumn(
                name: "EventsSummaryJson",
                table: "SyncHistory");
        }
    }
}
