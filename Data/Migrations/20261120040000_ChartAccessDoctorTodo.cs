using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261120040000_ChartAccessDoctorTodo")]
    public class ChartAccessDoctorTodo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ToDo",
                table: "ChartAccessRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Doctors",
                table: "ChartAccessRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ToDo",
                table: "ChartAccessGrants",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Doctors",
                table: "ChartAccessGrants",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ToDo", table: "ChartAccessRequests");
            migrationBuilder.DropColumn(name: "Doctors", table: "ChartAccessRequests");
            migrationBuilder.DropColumn(name: "ToDo", table: "ChartAccessGrants");
            migrationBuilder.DropColumn(name: "Doctors", table: "ChartAccessGrants");
        }
    }
}
