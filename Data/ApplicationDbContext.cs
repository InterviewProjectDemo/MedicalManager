using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();
    public DbSet<BloodPressureReading> BloodPressureReadings => Set<BloodPressureReading>();
    public DbSet<SugarReading> SugarReadings => Set<SugarReading>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<MedicationSchedule> MedicationSchedules => Set<MedicationSchedule>();
    public DbSet<MedicationAdministration> MedicationAdministrations => Set<MedicationAdministration>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<LabReport> LabReports => Set<LabReport>();
    public DbSet<LabResult> LabResults => Set<LabResult>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<PatientProfile>()
            .HasOne(x => x.User)
            .WithOne(u => u.Profile)
            .HasForeignKey<PatientProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PatientProfile>().HasIndex(x => x.UserId).IsUnique();

        builder.Entity<BloodPressureReading>()
            .HasOne(x => x.User)
            .WithMany(u => u.BloodPressureReadings)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SugarReading>()
            .HasOne(x => x.User)
            .WithMany(u => u.SugarReadings)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Medication>()
            .HasOne(x => x.User)
            .WithMany(u => u.Medications)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Appointment>()
            .HasOne(x => x.User)
            .WithMany(u => u.Appointments)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MedicationSchedule>()
            .HasOne(x => x.Medication)
            .WithMany(m => m.Schedules)
            .HasForeignKey(x => x.MedicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MedicationSchedule>()
            .HasIndex(x => new { x.MedicationId, x.TimeSlot })
            .IsUnique();

        builder.Entity<MedicationAdministration>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MedicationAdministration>()
            .HasOne(x => x.Medication)
            .WithMany()
            .HasForeignKey(x => x.MedicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MedicationAdministration>()
            .HasIndex(x => new { x.UserId, x.MedicationId, x.ScheduledDate, x.TimeSlot })
            .IsUnique();

        builder.Entity<BloodPressureReading>().HasIndex(x => new { x.UserId, x.RecordedAt });
        builder.Entity<SugarReading>().HasIndex(x => new { x.UserId, x.RecordedAt });
        builder.Entity<Appointment>().HasIndex(x => new { x.UserId, x.StartsAt });

        builder.Entity<Doctor>()
            .HasOne(x => x.User)
            .WithMany(u => u.Doctors)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Doctor>().HasIndex(x => new { x.UserId, x.Name });

        builder.Entity<LabReport>()
            .HasOne(x => x.User)
            .WithMany(u => u.LabReports)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<LabResult>()
            .HasOne(x => x.LabReport)
            .WithMany(r => r.Results)
            .HasForeignKey(x => x.LabReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<LabReport>().HasIndex(x => new { x.UserId, x.ReportDate });
        builder.Entity<LabResult>().HasIndex(x => new { x.LabReportId, x.TestCode }).IsUnique();
    }
}
