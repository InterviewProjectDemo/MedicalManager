using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261119040000_VirtualVisit")]
    public class VirtualVisit : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "LabReports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderingDoctorName",
                table: "LabReports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OrderedAt",
                table: "LabReports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderedTestCodes",
                table: "LabReports",
                type: "TEXT",
                nullable: true);

            CreateVisitTable(migrationBuilder, "ClinicalNotes", """
                "AuthorName" TEXT NOT NULL,
                "Body" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL
                """);
            migrationBuilder.CreateIndex(
                name: "IX_ClinicalNotes_PatientUserId_CreatedAt",
                table: "ClinicalNotes",
                columns: ["PatientUserId", "CreatedAt"]);

            CreateVisitTable(migrationBuilder, "VisitMessages", """
                "SenderUserId" TEXT NOT NULL,
                "Body" TEXT NOT NULL,
                "SentAt" TEXT NOT NULL
                """);
            migrationBuilder.CreateIndex(
                name: "IX_VisitMessages_PatientUserId_DoctorUserId_SentAt",
                table: "VisitMessages",
                columns: ["PatientUserId", "DoctorUserId", "SentAt"]);

            CreateVisitTable(migrationBuilder, "VideoVisits", """
                "DoctorName" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "StartedAt" TEXT NULL,
                "EndedAt" TEXT NULL,
                "Status" TEXT NOT NULL
                """);
            migrationBuilder.CreateIndex(
                name: "IX_VideoVisits_PatientUserId_DoctorUserId_CreatedAt",
                table: "VideoVisits",
                columns: ["PatientUserId", "DoctorUserId", "CreatedAt"]);

            CreateVisitTable(migrationBuilder, "AfterVisitSummaries", """
                "AuthorName" TEXT NOT NULL,
                "Body" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL
                """);
            migrationBuilder.CreateIndex(
                name: "IX_AfterVisitSummaries_PatientUserId_CreatedAt",
                table: "AfterVisitSummaries",
                columns: ["PatientUserId", "CreatedAt"]);

            CreateVisitTable(migrationBuilder, "ReferralOrders", """
                "AuthorName" TEXT NOT NULL,
                "Specialty" TEXT NOT NULL,
                "ClinicianName" TEXT NULL,
                "Reason" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL
                """);
            migrationBuilder.CreateIndex(
                name: "IX_ReferralOrders_PatientUserId_CreatedAt",
                table: "ReferralOrders",
                columns: ["PatientUserId", "CreatedAt"]);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ReferralOrders");
            migrationBuilder.DropTable(name: "AfterVisitSummaries");
            migrationBuilder.DropTable(name: "VideoVisits");
            migrationBuilder.DropTable(name: "VisitMessages");
            migrationBuilder.DropTable(name: "ClinicalNotes");
            migrationBuilder.DropColumn(name: "OrderedTestCodes", table: "LabReports");
            migrationBuilder.DropColumn(name: "OrderedAt", table: "LabReports");
            migrationBuilder.DropColumn(name: "OrderingDoctorName", table: "LabReports");
            migrationBuilder.DropColumn(name: "Status", table: "LabReports");
        }

        private static void CreateVisitTable(MigrationBuilder migrationBuilder, string name, string extraColumns)
        {
            migrationBuilder.Sql(
                $"""
                CREATE TABLE IF NOT EXISTS "{name}" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_{name}" PRIMARY KEY AUTOINCREMENT,
                    "PatientUserId" TEXT NOT NULL,
                    "DoctorUserId" TEXT NOT NULL,
                    {extraColumns},
                    CONSTRAINT "FK_{name}_AspNetUsers_PatientUserId" FOREIGN KEY ("PatientUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_{name}_AspNetUsers_DoctorUserId" FOREIGN KEY ("DoctorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
                );
                """);
        }
    }
}
