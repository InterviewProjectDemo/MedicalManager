using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261019040000_MedicationDaysAndAdministerTime")]
    public class MedicationDaysAndAdministerTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Days",
                table: "Medications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 127);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "AdministerAt",
                table: "MedicationSchedules",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Days", table: "Medications");
            migrationBuilder.DropColumn(name: "AdministerAt", table: "MedicationSchedules");
        }
    }
}
