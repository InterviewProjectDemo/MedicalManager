using MedicalManager.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260910160000_PatientProfilesAndMedicationNotes")]
public partial class PatientProfilesAndMedicationNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "Instructions",
            table: "Medications",
            newName: "Notes");

        migrationBuilder.CreateTable(
            name: "PatientProfiles",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                DateOfBirth = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Sex = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                Phone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                Notes = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PatientProfiles", x => x.Id);
                table.ForeignKey(
                    name: "FK_PatientProfiles_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PatientProfiles_UserId",
            table: "PatientProfiles",
            column: "UserId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PatientProfiles");

        migrationBuilder.RenameColumn(
            name: "Notes",
            table: "Medications",
            newName: "Instructions");
    }
}
