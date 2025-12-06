using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleverSyncSOS.Core.Database.SessionDb.Migrations
{
    /// <inheritdoc />
    public partial class AddKeyVaultPrefixMappings_SessionDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeyVaultSchoolPrefix",
                table: "Schools",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "KeyVaultDistrictPrefix",
                table: "Districts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeyVaultSchoolPrefix",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "KeyVaultDistrictPrefix",
                table: "Districts");
        }
    }
}
