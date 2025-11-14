using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleverSyncSOS.Core.Database.SessionDb.Migrations
{
    /// <inheritdoc />
    public partial class ChangeDistrictIdToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Schools_Districts_DistrictId",
                table: "Schools");

            migrationBuilder.AlterColumn<string>(
                name: "DistrictId",
                table: "Schools",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Districts_CleverDistrictId",
                table: "Districts",
                column: "CleverDistrictId");

            migrationBuilder.AddForeignKey(
                name: "FK_Schools_Districts_DistrictId",
                table: "Schools",
                column: "DistrictId",
                principalTable: "Districts",
                principalColumn: "CleverDistrictId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Schools_Districts_DistrictId",
                table: "Schools");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Districts_CleverDistrictId",
                table: "Districts");

            migrationBuilder.AlterColumn<int>(
                name: "DistrictId",
                table: "Schools",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddForeignKey(
                name: "FK_Schools_Districts_DistrictId",
                table: "Schools",
                column: "DistrictId",
                principalTable: "Districts",
                principalColumn: "DistrictId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
