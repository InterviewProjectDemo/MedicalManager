using MedicalManager.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260910190000_MedicationAdministrationRecordedByName")]
public partial class MedicationAdministrationRecordedByName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RecordedByName",
            table: "MedicationAdministrations",
            type: "TEXT",
            maxLength: 120,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RecordedByName",
            table: "MedicationAdministrations");
    }
}
