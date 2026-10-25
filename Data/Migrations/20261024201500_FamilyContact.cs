using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261024201500_FamilyContact")]
    public class FamilyContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FamilyContactName",
                table: "PatientProfiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FamilyContactPhone",
                table: "PatientProfiles",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FamilyContactPhotoContentType",
                table: "PatientProfiles",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "FamilyContactPhotoData",
                table: "PatientProfiles",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FamilyContactPhotoUpdatedAt",
                table: "PatientProfiles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "FamilyContactName", table: "PatientProfiles");
            migrationBuilder.DropColumn(name: "FamilyContactPhone", table: "PatientProfiles");
            migrationBuilder.DropColumn(name: "FamilyContactPhotoContentType", table: "PatientProfiles");
            migrationBuilder.DropColumn(name: "FamilyContactPhotoData", table: "PatientProfiles");
            migrationBuilder.DropColumn(name: "FamilyContactPhotoUpdatedAt", table: "PatientProfiles");
        }
    }
}
