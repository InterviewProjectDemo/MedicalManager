using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261031233000_ChartAccess")]
    public class ChartAccess : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChartAccessRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DoctorUserId = table.Column<string>(type: "TEXT", nullable: false),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedName = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedDateOfBirth = table.Column<string>(type: "TEXT", nullable: true),
                    Lab = table.Column<int>(type: "INTEGER", nullable: false),
                    BloodPressure = table.Column<int>(type: "INTEGER", nullable: false),
                    Sugar = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CodeSalt = table.Column<string>(type: "TEXT", nullable: true),
                    CodeHash = table.Column<string>(type: "TEXT", nullable: true),
                    CodeExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CodeAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChartAccessRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChartAccessRequests_AspNetUsers_DoctorUserId",
                        column: x => x.DoctorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChartAccessRequests_AspNetUsers_PatientUserId",
                        column: x => x.PatientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChartAccessGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DoctorUserId = table.Column<string>(type: "TEXT", nullable: false),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Lab = table.Column<int>(type: "INTEGER", nullable: false),
                    BloodPressure = table.Column<int>(type: "INTEGER", nullable: false),
                    Sugar = table.Column<int>(type: "INTEGER", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChartAccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChartAccessGrants_AspNetUsers_DoctorUserId",
                        column: x => x.DoctorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChartAccessGrants_AspNetUsers_PatientUserId",
                        column: x => x.PatientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChartAccessRequests_DoctorUserId_Status",
                table: "ChartAccessRequests",
                columns: new[] { "DoctorUserId", "Status" });
            migrationBuilder.CreateIndex(
                name: "IX_ChartAccessRequests_PatientUserId_Status",
                table: "ChartAccessRequests",
                columns: new[] { "PatientUserId", "Status" });
            migrationBuilder.CreateIndex(
                name: "IX_ChartAccessGrants_DoctorUserId_PatientUserId",
                table: "ChartAccessGrants",
                columns: new[] { "DoctorUserId", "PatientUserId" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_ChartAccessGrants_PatientUserId",
                table: "ChartAccessGrants",
                column: "PatientUserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ChartAccessRequests");
            migrationBuilder.DropTable(name: "ChartAccessGrants");
        }
    }
}
