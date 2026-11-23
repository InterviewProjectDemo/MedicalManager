using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261122203000_ScheduleSeats")]
    public class ScheduleSeats : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduleSeats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DoctorUserId = table.Column<string>(type: "TEXT", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: true),
                    PatientConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    DoctorConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: true),
                    AppointmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleSeats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSeats_DoctorUserId_StartsAt",
                table: "ScheduleSeats",
                columns: ["DoctorUserId", "StartsAt"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSeats_PatientUserId",
                table: "ScheduleSeats",
                column: "PatientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSeats_AppointmentId",
                table: "ScheduleSeats",
                column: "AppointmentId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ScheduleSeats");
        }
    }
}
