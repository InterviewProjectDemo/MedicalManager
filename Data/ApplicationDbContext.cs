using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MedicalManager.Data.Security;

namespace MedicalManager.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly IPhiProtector _phi;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IPhiProtector phi)
        : base(options)
    {
        _phi = phi;
    }
    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();
    public DbSet<BloodPressureReading> BloodPressureReadings => Set<BloodPressureReading>();
    public DbSet<SugarReading> SugarReadings => Set<SugarReading>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<MedicationSchedule> MedicationSchedules => Set<MedicationSchedule>();
    public DbSet<MedicationAdministration> MedicationAdministrations => Set<MedicationAdministration>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentReminder> AppointmentReminders => Set<AppointmentReminder>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<LabReport> LabReports => Set<LabReport>();
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<ToDoItem> ToDoItems => Set<ToDoItem>();
    public DbSet<UserDashboardLayout> UserDashboardLayouts => Set<UserDashboardLayout>();

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

        builder.Entity<AppointmentReminder>()
            .HasOne(x => x.Appointment)
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<AppointmentReminder>()
            .HasIndex(x => new { x.AppointmentId, x.Kind, x.Channel, x.StartsAtSnapshot })
            .IsUnique();

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

        builder.Entity<ToDoItem>()
            .HasOne(x => x.User)
            .WithMany(u => u.ToDoItems)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ToDoItem>().HasIndex(x => new { x.UserId, x.FinishBy });

        builder.Entity<UserDashboardLayout>()
            .HasOne(x => x.User)
            .WithOne(u => u.DashboardLayout)
            .HasForeignKey<UserDashboardLayout>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UserDashboardLayout>().HasIndex(x => x.UserId).IsUnique();

        ApplyPhiEncryption(builder);
    }

    private void ApplyPhiEncryption(ModelBuilder builder)
    {
        var required = PhiValueConverters.RequiredString(_phi);
        var optional = PhiValueConverters.OptionalString(_phi);
        var bytes = PhiValueConverters.OptionalBytes(_phi);
        var date = PhiValueConverters.OptionalDate(_phi);

        builder.Entity<ApplicationUser>().Property(x => x.FullName)
            .HasConversion(required)
            .HasColumnType("text");
        builder.Entity<ApplicationUser>().Property(x => x.PhoneNumber)
            .HasConversion(optional)
            .HasColumnType("text");

        builder.Entity<PatientProfile>().Property(x => x.Sex).HasConversion(optional).HasColumnType("text");
        builder.Entity<PatientProfile>().Property(x => x.Phone).HasConversion(optional).HasColumnType("text");
        builder.Entity<PatientProfile>().Property(x => x.HomeAddress).HasConversion(optional).HasColumnType("text");
        builder.Entity<PatientProfile>().Property(x => x.NamePronunciation).HasConversion(optional).HasColumnType("text");
        builder.Entity<PatientProfile>().Property(x => x.Notes).HasConversion(optional).HasColumnType("text");
        builder.Entity<PatientProfile>().Property(x => x.DateOfBirth).HasConversion(date).HasColumnType("text");
        builder.Entity<PatientProfile>().Property(x => x.ProfilePhotoData).HasConversion(bytes);

        builder.Entity<BloodPressureReading>().Property(x => x.Notes).HasConversion(optional).HasColumnType("text");
        builder.Entity<SugarReading>().Property(x => x.Notes).HasConversion(optional).HasColumnType("text");

        builder.Entity<Medication>().Property(x => x.Name).HasConversion(required).HasColumnType("text");
        builder.Entity<Medication>().Property(x => x.Dosage).HasConversion(required).HasColumnType("text");
        builder.Entity<Medication>().Property(x => x.Frequency).HasConversion(required).HasColumnType("text");
        builder.Entity<Medication>().Property(x => x.Purpose).HasConversion(optional).HasColumnType("text");
        builder.Entity<Medication>().Property(x => x.Notes).HasConversion(optional).HasColumnType("text");
        builder.Entity<Medication>().Property(x => x.PrescribedBy).HasConversion(required).HasColumnType("text");

        builder.Entity<Appointment>().Property(x => x.Title).HasConversion(required).HasColumnType("text");
        builder.Entity<Appointment>().Property(x => x.ProviderName).HasConversion(required).HasColumnType("text");
        builder.Entity<Appointment>().Property(x => x.Location).HasConversion(required).HasColumnType("text");
        builder.Entity<Appointment>().Property(x => x.Purpose).HasConversion(optional).HasColumnType("text");
        builder.Entity<Appointment>().Property(x => x.Notes).HasConversion(optional).HasColumnType("text");

        builder.Entity<Doctor>().Property(x => x.Name).HasConversion(required).HasColumnType("text");
        builder.Entity<Doctor>().Property(x => x.Specialty).HasConversion(required).HasColumnType("text");
        builder.Entity<Doctor>().Property(x => x.PhoneNumber).HasConversion(required).HasColumnType("text");

        builder.Entity<LabReport>().Property(x => x.Notes).HasConversion(optional).HasColumnType("text");
        builder.Entity<ToDoItem>().Property(x => x.Description).HasConversion(required).HasColumnType("text");
        builder.Entity<ToDoItem>().Property(x => x.Notes).HasConversion(optional).HasColumnType("text");
        builder.Entity<MedicationAdministration>().Property(x => x.RecordedByName)
            .HasConversion(optional)
            .HasColumnType("text");
    }
}
