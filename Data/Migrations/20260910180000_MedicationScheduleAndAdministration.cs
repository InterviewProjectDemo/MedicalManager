using MedicalManager.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260910180000_MedicationScheduleAndAdministration")]
public partial class MedicationScheduleAndAdministration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MedicationSchedules",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                MedicationId = table.Column<int>(type: "INTEGER", nullable: false),
                TimeSlot = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MedicationSchedules", x => x.Id);
                table.ForeignKey(
                    name: "FK_MedicationSchedules_Medications_MedicationId",
                    column: x => x.MedicationId,
                    principalTable: "Medications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MedicationAdministrations",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                MedicationId = table.Column<int>(type: "INTEGER", nullable: false),
                ScheduledDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                TimeSlot = table.Column<int>(type: "INTEGER", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                UpdatedByUserId = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MedicationAdministrations", x => x.Id);
                table.ForeignKey(
                    name: "FK_MedicationAdministrations_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_MedicationAdministrations_Medications_MedicationId",
                    column: x => x.MedicationId,
                    principalTable: "Medications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MedicationSchedules_MedicationId_TimeSlot",
            table: "MedicationSchedules",
            columns: new[] { "MedicationId", "TimeSlot" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MedicationAdministrations_MedicationId",
            table: "MedicationAdministrations",
            column: "MedicationId");

        migrationBuilder.CreateIndex(
            name: "IX_MedicationAdministrations_UserId_MedicationId_ScheduledDate_TimeSlot",
            table: "MedicationAdministrations",
            columns: new[] { "UserId", "MedicationId", "ScheduledDate", "TimeSlot" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MedicationAdministrations");
        migrationBuilder.DropTable(name: "MedicationSchedules");
    }
}
