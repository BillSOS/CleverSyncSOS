using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleverSyncSOS.Core.Database.SessionDb.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalTimeZoneToDistrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LocalTimeZone",
                table: "Districts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Eastern Standard Time");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocalTimeZone",
                table: "Districts");
        }
    }
}
