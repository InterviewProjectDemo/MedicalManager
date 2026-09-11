using MedicalManager.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260910210000_RepairDoctorsSchema")]
public partial class RepairDoctorsSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "Doctors" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Doctors" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Specialty" TEXT NOT NULL,
                "PhoneNumber" TEXT NOT NULL,
                "Source" INTEGER NOT NULL,
                "IsAutoDiscovered" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_Doctors_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Doctors_UserId_Name" ON "Doctors" ("UserId", "Name");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
