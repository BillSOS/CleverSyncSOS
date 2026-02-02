using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleverSyncSOS.Core.Database.SchoolDb.Migrations
{
    /// <inheritdoc />
    public partial class MakeSectionCourseOptional_SchoolDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    CourseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CleverCourseId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SchoolId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GradeLevels = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastModifiedInClever = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeactivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.CourseId);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    SectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CleverSectionId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CourseId = table.Column<int>(type: "int", nullable: true),
                    SchoolId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Period = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CourseName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CourseNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SubjectNormalized = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TermId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    TermName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TermStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TermEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Grade = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastModifiedInClever = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeactivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => x.SectionId);
                    table.ForeignKey(
                        name: "FK_Sections_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "CourseId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StudentSections",
                columns: table => new
                {
                    StudentSectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentSections", x => x.StudentSectionId);
                    table.ForeignKey(
                        name: "FK_StudentSections_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "SectionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentSections_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeacherSections",
                columns: table => new
                {
                    TeacherSectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeacherId = table.Column<int>(type: "int", nullable: false),
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherSections", x => x.TeacherSectionId);
                    table.ForeignKey(
                        name: "FK_TeacherSections_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "SectionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeacherSections_Teachers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Teachers",
                        principalColumn: "TeacherId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Courses_CleverCourseId",
                table: "Courses",
                column: "CleverCourseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_IsActive",
                table: "Courses",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_SchoolId",
                table: "Courses",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_Subject",
                table: "Courses",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_CleverSectionId",
                table: "Sections",
                column: "CleverSectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sections_CourseId",
                table: "Sections",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_Grade",
                table: "Sections",
                column: "Grade");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_IsActive",
                table: "Sections",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_SchoolId",
                table: "Sections",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_Subject",
                table: "Sections",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_TermId",
                table: "Sections",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSections_SectionId",
                table: "StudentSections",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSections_StudentId",
                table: "StudentSections",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSections_StudentId_SectionId",
                table: "StudentSections",
                columns: new[] { "StudentId", "SectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherSections_IsPrimary",
                table: "TeacherSections",
                column: "IsPrimary");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherSections_SectionId",
                table: "TeacherSections",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherSections_TeacherId",
                table: "TeacherSections",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherSections_TeacherId_SectionId",
                table: "TeacherSections",
                columns: new[] { "TeacherId", "SectionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentSections");

            migrationBuilder.DropTable(
                name: "TeacherSections");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "Courses");
        }
    }
}
