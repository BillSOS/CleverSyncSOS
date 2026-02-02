using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleverSyncSOS.Core.Database.SessionDb.Migrations
{
    /// <inheritdoc />
    public partial class AddEventsLog_SessionDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventsLogs",
                columns: table => new
                {
                    EventsLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CheckedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CheckedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApiAccessible = table.Column<bool>(type: "bit", nullable: false),
                    EventCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    DeletedCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LatestEventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EarliestEventTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LatestEventTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ObjectTypeSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SampleEventsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventsLogs", x => x.EventsLogId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventsLogs_CheckedAt",
                table: "EventsLogs",
                column: "CheckedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventsLogs");
        }
    }
}
