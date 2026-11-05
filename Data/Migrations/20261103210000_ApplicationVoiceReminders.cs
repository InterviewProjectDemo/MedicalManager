using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalManager.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261103210000_ApplicationVoiceReminders")]
    public class ApplicationVoiceReminders : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ApplicationRemindersEnabled",
                table: "PatientProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ApplicationVoiceReminders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ReminderKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SpokenAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationVoiceReminders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationVoiceReminders_UserId_ReminderKey",
                table: "ApplicationVoiceReminders",
                columns: new[] { "UserId", "ReminderKey" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ApplicationVoiceReminders");
            migrationBuilder.DropColumn(name: "ApplicationRemindersEnabled", table: "PatientProfiles");
        }
    }
}
