using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Migrations;

[DbContext(typeof(Data.ApplicationDbContext))]
[Migration("20260909120000_HealthRecords")]
public partial class HealthRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FullName",
            table: "AspNetUsers",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateTable(
            name: "Appointments",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                ProviderName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Location = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                StartsAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                EndsAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                Notes = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Appointments", x => x.Id);
                table.ForeignKey(
                    name: "FK_Appointments_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BloodPressureReadings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                Systolic = table.Column<int>(type: "INTEGER", nullable: false),
                Diastolic = table.Column<int>(type: "INTEGER", nullable: false),
                Pulse = table.Column<int>(type: "INTEGER", nullable: true),
                RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                Notes = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BloodPressureReadings", x => x.Id);
                table.ForeignKey(
                    name: "FK_BloodPressureReadings_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Medications",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Dosage = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                Frequency = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                Instructions = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Medications", x => x.Id);
                table.ForeignKey(
                    name: "FK_Medications_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SugarReadings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                Value = table.Column<decimal>(type: "TEXT", nullable: false),
                Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                Notes = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SugarReadings", x => x.Id);
                table.ForeignKey(
                    name: "FK_SugarReadings_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Appointments_UserId_StartsAt",
            table: "Appointments",
            columns: new[] { "UserId", "StartsAt" });

        migrationBuilder.CreateIndex(
            name: "IX_BloodPressureReadings_UserId_RecordedAt",
            table: "BloodPressureReadings",
            columns: new[] { "UserId", "RecordedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_SugarReadings_UserId_RecordedAt",
            table: "SugarReadings",
            columns: new[] { "UserId", "RecordedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Medications_UserId",
            table: "Medications",
            column: "UserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Appointments");
        migrationBuilder.DropTable(name: "BloodPressureReadings");
        migrationBuilder.DropTable(name: "Medications");
        migrationBuilder.DropTable(name: "SugarReadings");
        migrationBuilder.DropColumn(name: "FullName", table: "AspNetUsers");
    }
}
