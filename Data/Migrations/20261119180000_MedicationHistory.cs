using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261119180000_MedicationHistory")]
    public class MedicationHistory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrescribingDoctorUserId",
                table: "Medications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MedicationChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MedicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ActorName = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicationChanges_Medications_MedicationId",
                        column: x => x.MedicationId,
                        principalTable: "Medications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicationChanges_MedicationId_ChangedAt",
                table: "MedicationChanges",
                columns: ["MedicationId", "ChangedAt"]);

            migrationBuilder.CreateIndex(
                name: "IX_MedicationChanges_PatientUserId_ChangedAt",
                table: "MedicationChanges",
                columns: ["PatientUserId", "ChangedAt"]);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MedicationChanges");
            migrationBuilder.DropColumn(name: "PrescribingDoctorUserId", table: "Medications");
        }
    }
}
