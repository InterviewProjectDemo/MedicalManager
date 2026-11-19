using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261118210000_ChartAccessMedication")]
    public class ChartAccessMedication : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Medication",
                table: "ChartAccessRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Medication",
                table: "ChartAccessGrants",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Medication", table: "ChartAccessRequests");
            migrationBuilder.DropColumn(name: "Medication", table: "ChartAccessGrants");
        }
    }
}
