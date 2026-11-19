using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261119203000_VisitNoteSections")]
    public class VisitNoteSections : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "ClinicalNotes",
                type: "TEXT",
                nullable: false,
                defaultValue: "Doctor");

            migrationBuilder.AddColumn<string>(
                name: "AuthorUserId",
                table: "ClinicalNotes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AuthorRole",
                table: "ClinicalNotes",
                type: "TEXT",
                nullable: false,
                defaultValue: "Doctor");

            migrationBuilder.Sql(
                """UPDATE "ClinicalNotes" SET "AuthorUserId" = "DoctorUserId" WHERE "AuthorUserId" = '';""");

            migrationBuilder.AddColumn<string>(
                name: "Specialty",
                table: "VideoVisits",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Specialty", table: "VideoVisits");
            migrationBuilder.DropColumn(name: "AuthorRole", table: "ClinicalNotes");
            migrationBuilder.DropColumn(name: "AuthorUserId", table: "ClinicalNotes");
            migrationBuilder.DropColumn(name: "Section", table: "ClinicalNotes");
        }
    }
}
