using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public static class IdentitySeeder
{
    public const string DemoPatientEmail = "patient@medicalmanager.local";
    public const string DemoPatientPassword = "Patient123!";
    public const string DemoDoctorEmail = "doctor@medicalmanager.local";
    public const string DemoDoctorPassword = "Doctor123!";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsLock\"");
        }
        catch (SqliteException)
        {
            // Lock table may not exist yet on first run.
        }

        await db.Database.MigrateAsync();

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole(role));
            }
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var patient = await EnsureUserAsync(users, DemoPatientEmail, DemoPatientPassword, "Alex Rivera", AppRoles.Patient);
        await EnsureUserAsync(users, DemoDoctorEmail, DemoDoctorPassword, "Dr. Morgan Chen", AppRoles.Doctor);

        if (!await db.PatientProfiles.AnyAsync(x => x.UserId == patient.Id))
        {
            db.PatientProfiles.Add(new PatientProfile
            {
                UserId = patient.Id,
                DateOfBirth = new DateOnly(1988, 4, 12),
                Sex = "Female",
                Phone = "555-0142",
                Notes = "Demo patient monitoring blood pressure and glucose."
            });
        }

        if (!await db.BloodPressureReadings.AnyAsync(x => x.UserId == patient.Id))
        {
            var today = DateTime.Today;
            db.BloodPressureReadings.AddRange(
                new BloodPressureReading { UserId = patient.Id, Systolic = 118, Diastolic = 76, Pulse = 68, RecordedAt = today.AddDays(-9).AddHours(8), Notes = "Morning reading" },
                new BloodPressureReading { UserId = patient.Id, Systolic = 122, Diastolic = 78, Pulse = 70, RecordedAt = today.AddDays(-8).AddHours(8) },
                new BloodPressureReading { UserId = patient.Id, Systolic = 128, Diastolic = 82, Pulse = 72, RecordedAt = today.AddDays(-7).AddHours(8) },
                new BloodPressureReading { UserId = patient.Id, Systolic = 134, Diastolic = 86, Pulse = 74, RecordedAt = today.AddDays(-6).AddHours(8), Notes = "After a salty dinner" },
                new BloodPressureReading { UserId = patient.Id, Systolic = 126, Diastolic = 80, Pulse = 69, RecordedAt = today.AddDays(-5).AddHours(8) },
                new BloodPressureReading { UserId = patient.Id, Systolic = 142, Diastolic = 91, Pulse = 78, RecordedAt = today.AddDays(-4).AddHours(19), Notes = "Evening spike" },
                new BloodPressureReading { UserId = patient.Id, Systolic = 130, Diastolic = 84, Pulse = 71, RecordedAt = today.AddDays(-3).AddHours(8) },
                new BloodPressureReading { UserId = patient.Id, Systolic = 121, Diastolic = 77, Pulse = 66, RecordedAt = today.AddDays(-2).AddHours(8) },
                new BloodPressureReading { UserId = patient.Id, Systolic = 119, Diastolic = 75, Pulse = 67, RecordedAt = today.AddDays(-1).AddHours(8) },
                new BloodPressureReading { UserId = patient.Id, Systolic = 124, Diastolic = 79, Pulse = 68, RecordedAt = today.AddHours(8) });

            db.SugarReadings.AddRange(
                new SugarReading { UserId = patient.Id, Value = 92, Kind = SugarKind.Fasting, RecordedAt = today.AddDays(-9).AddHours(7) },
                new SugarReading { UserId = patient.Id, Value = 138, Kind = SugarKind.AfterMeal, RecordedAt = today.AddDays(-9).AddHours(13) },
                new SugarReading { UserId = patient.Id, Value = 98, Kind = SugarKind.Fasting, RecordedAt = today.AddDays(-7).AddHours(7) },
                new SugarReading { UserId = patient.Id, Value = 162, Kind = SugarKind.AfterMeal, RecordedAt = today.AddDays(-6).AddHours(13), Notes = "Pasta lunch" },
                new SugarReading { UserId = patient.Id, Value = 104, Kind = SugarKind.Fasting, RecordedAt = today.AddDays(-5).AddHours(7) },
                new SugarReading { UserId = patient.Id, Value = 118, Kind = SugarKind.Random, RecordedAt = today.AddDays(-4).AddHours(16) },
                new SugarReading { UserId = patient.Id, Value = 89, Kind = SugarKind.Fasting, RecordedAt = today.AddDays(-3).AddHours(7) },
                new SugarReading { UserId = patient.Id, Value = 146, Kind = SugarKind.AfterMeal, RecordedAt = today.AddDays(-2).AddHours(13) },
                new SugarReading { UserId = patient.Id, Value = 95, Kind = SugarKind.Fasting, RecordedAt = today.AddDays(-1).AddHours(7) },
                new SugarReading { UserId = patient.Id, Value = 132, Kind = SugarKind.AfterMeal, RecordedAt = today.AddHours(13) });

            var lisinopril = new Medication
            {
                UserId = patient.Id,
                Name = "Lisinopril",
                Dosage = "10 mg",
                Frequency = "Once daily",
                PrescribedBy = "Dr. Morgan Chen",
                Notes = "Take in the morning with water.",
                IsActive = true,
                StartDate = DateOnly.FromDateTime(today.AddMonths(-6))
            };
            var metformin = new Medication
            {
                UserId = patient.Id,
                Name = "Metformin",
                Dosage = "500 mg",
                Frequency = "Twice daily",
                PrescribedBy = "Dr. Morgan Chen",
                Notes = "Take with breakfast and dinner.",
                IsActive = true,
                StartDate = DateOnly.FromDateTime(today.AddMonths(-4))
            };
            var atorvastatin = new Medication
            {
                UserId = patient.Id,
                Name = "Atorvastatin",
                Dosage = "20 mg",
                Frequency = "Once daily at bedtime",
                PrescribedBy = "Dr. Sarah Kim",
                Notes = "Cholesterol support.",
                IsActive = true,
                StartDate = DateOnly.FromDateTime(today.AddMonths(-2))
            };
            db.Medications.AddRange(
                lisinopril,
                metformin,
                atorvastatin,
                new Medication
                {
                    UserId = patient.Id,
                    Name = "Ibuprofen",
                    Dosage = "200 mg",
                    Frequency = "As needed",
                    Notes = "Stopped after knee pain resolved.",
                    IsActive = false,
                    StartDate = DateOnly.FromDateTime(today.AddMonths(-1)),
                    EndDate = DateOnly.FromDateTime(today.AddDays(-10))
                });

            await db.SaveChangesAsync();

            db.MedicationSchedules.AddRange(
                new MedicationSchedule { MedicationId = lisinopril.Id, TimeSlot = MedicationTimeSlot.Morning },
                new MedicationSchedule { MedicationId = metformin.Id, TimeSlot = MedicationTimeSlot.Morning },
                new MedicationSchedule { MedicationId = metformin.Id, TimeSlot = MedicationTimeSlot.Evening },
                new MedicationSchedule { MedicationId = atorvastatin.Id, TimeSlot = MedicationTimeSlot.Bedtime });

            db.Appointments.AddRange(
                new Appointment
                {
                    UserId = patient.Id,
                    Title = "Annual physical",
                    ProviderName = "Dr. Morgan Chen",
                    Location = "Main Clinic, Room 214",
                    StartsAt = today.AddDays(-21).AddHours(9),
                    EndsAt = today.AddDays(-21).AddHours(9).AddMinutes(45),
                    Status = AppointmentStatus.Completed,
                    Notes = "Discussed BP home log."
                },
                new Appointment
                {
                    UserId = patient.Id,
                    Title = "Nutrition consult",
                    ProviderName = "Riley Patel, RD",
                    Location = "Wellness Center",
                    StartsAt = today.AddDays(-8).AddHours(14),
                    EndsAt = today.AddDays(-8).AddHours(15),
                    Status = AppointmentStatus.Cancelled,
                    Notes = "Rescheduled by patient."
                },
                new Appointment
                {
                    UserId = patient.Id,
                    Title = "Blood pressure check",
                    ProviderName = "Dr. Morgan Chen",
                    Location = "Main Clinic, Room 214",
                    StartsAt = today.AddHours(14),
                    EndsAt = today.AddHours(14).AddMinutes(30),
                    Status = AppointmentStatus.Scheduled,
                    Notes = "Review home BP log."
                },
                new Appointment
                {
                    UserId = patient.Id,
                    Title = "Follow-up visit",
                    ProviderName = "Dr. Morgan Chen",
                    Location = "Main Clinic, Room 214",
                    StartsAt = today.AddDays(3).AddHours(10),
                    EndsAt = today.AddDays(3).AddHours(10).AddMinutes(30),
                    Status = AppointmentStatus.Scheduled
                },
                new Appointment
                {
                    UserId = patient.Id,
                    Title = "Lab work",
                    ProviderName = "Community Lab",
                    Location = "West Wing, Lab 2",
                    StartsAt = today.AddDays(10).AddHours(8),
                    EndsAt = today.AddDays(10).AddHours(8).AddMinutes(20),
                    Status = AppointmentStatus.Scheduled,
                    Notes = "Fasting glucose and lipid panel."
                });
        }

        await EnsureDemoMedicationSchedulesAsync(db, patient.Id);
        await EnsureDemoLabReportsAsync(db, patient.Id);
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDemoLabReportsAsync(ApplicationDbContext db, string patientId)
    {
        if (await db.LabReports.AnyAsync(x => x.UserId == patientId)) return;

        var today = DateTime.Today;
        var sep2 = new LabReport
        {
            UserId = patientId,
            ReportDate = new DateOnly(today.Year, today.Month, 2),
            Notes = "Fasting lipid panel and CMP",
            CreatedAt = today.AddDays(-8).ToUniversalTime()
        };
        var sep10 = new LabReport
        {
            UserId = patientId,
            ReportDate = DateOnly.FromDateTime(today),
            Notes = "Follow-up glucose and A1c",
            CreatedAt = today.ToUniversalTime()
        };
        var aug15 = new LabReport
        {
            UserId = patientId,
            ReportDate = new DateOnly(today.Year, today.Month, 1).AddMonths(-1).AddDays(14),
            Notes = "Annual wellness labs",
            CreatedAt = today.AddMonths(-1).AddDays(-5).ToUniversalTime()
        };
        var jul20 = new LabReport
        {
            UserId = patientId,
            ReportDate = new DateOnly(today.Year, today.Month, 1).AddMonths(-2).AddDays(19),
            Notes = "Pre-visit baseline",
            CreatedAt = today.AddMonths(-2).ToUniversalTime()
        };

        db.LabReports.AddRange(sep2, sep10, aug15, jul20);
        await db.SaveChangesAsync();

        db.LabResults.AddRange(
            Result(sep2.Id, "glucose", 94),
            Result(sep2.Id, "hba1c", 5.4m),
            Result(sep2.Id, "total-cholesterol", 198),
            Result(sep2.Id, "ldl", 118),
            Result(sep2.Id, "hdl", 52),
            Result(sep2.Id, "triglycerides", 142),
            Result(sep2.Id, "creatinine", 0.9m),
            Result(sep2.Id, "bun", 14),
            Result(sep2.Id, "sodium", 140),
            Result(sep2.Id, "potassium", 4.1m),
            Result(sep2.Id, "hemoglobin", 13.6m),
            Result(sep2.Id, "wbc", 6.8m),
            Result(sep2.Id, "platelets", 245),
            Result(sep2.Id, "tsh", 2.1m),
            Result(sep2.Id, "alt", 22),
            Result(sep2.Id, "ast", 19),

            Result(sep10.Id, "glucose", 118),
            Result(sep10.Id, "hba1c", 6.1m),
            Result(sep10.Id, "ldl", 132),
            Result(sep10.Id, "hdl", 38),
            Result(sep10.Id, "triglycerides", 188),
            Result(sep10.Id, "creatinine", 1.0m),
            Result(sep10.Id, "alt", 62),

            Result(aug15.Id, "glucose", 88),
            Result(aug15.Id, "hba1c", 5.2m),
            Result(aug15.Id, "total-cholesterol", 210),
            Result(aug15.Id, "ldl", 128),
            Result(aug15.Id, "hdl", 48),
            Result(aug15.Id, "triglycerides", 165),
            Result(aug15.Id, "sodium", 138),
            Result(aug15.Id, "potassium", 5.3m),
            Result(aug15.Id, "hemoglobin", 11.8m),
            Result(aug15.Id, "wbc", 11.6m),
            Result(aug15.Id, "tsh", 4.6m),

            Result(jul20.Id, "glucose", 102),
            Result(jul20.Id, "total-cholesterol", 192),
            Result(jul20.Id, "ldl", 96),
            Result(jul20.Id, "hdl", 55),
            Result(jul20.Id, "creatinine", 0.8m),
            Result(jul20.Id, "bun", 18),
            Result(jul20.Id, "hemoglobin", 14.1m),
            Result(jul20.Id, "platelets", 148),
            Result(jul20.Id, "ast", 44));
    }

    private static LabResult Result(int reportId, string code, decimal value)
    {
        var def = LabTestDefinitions.Get(code);
        return new LabResult
        {
            LabReportId = reportId,
            TestCode = code,
            Value = value,
            Unit = def?.Unit
        };
    }

    private static async Task EnsureDemoMedicationSchedulesAsync(ApplicationDbContext db, string patientId)
    {
        if (await db.MedicationSchedules.AnyAsync()) return;

        var meds = await db.Medications
            .Where(x => x.UserId == patientId && x.IsActive)
            .ToListAsync();

        foreach (var med in meds)
        {
            var slots = med.Name switch
            {
                "Lisinopril" => new[] { MedicationTimeSlot.Morning },
                "Metformin" => new[] { MedicationTimeSlot.Morning, MedicationTimeSlot.Evening },
                "Atorvastatin" => new[] { MedicationTimeSlot.Bedtime },
                _ => Array.Empty<MedicationTimeSlot>()
            };

            foreach (var slot in slots)
            {
                db.MedicationSchedules.Add(new MedicationSchedule
                {
                    MedicationId = med.Id,
                    TimeSlot = slot
                });
            }
        }

        var today = DateTime.Today;
        if (!await db.Appointments.AnyAsync(x => x.UserId == patientId && x.StartsAt.Date == today))
        {
            db.Appointments.Add(new Appointment
            {
                UserId = patientId,
                Title = "Blood pressure check",
                ProviderName = "Dr. Morgan Chen",
                Location = "Main Clinic, Room 214",
                StartsAt = today.AddHours(14),
                EndsAt = today.AddHours(14).AddMinutes(30),
                Status = AppointmentStatus.Scheduled,
                Notes = "Review home BP log."
            });
        }
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> users,
        string email,
        string password,
        string fullName,
        string role)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName
            };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        if (!await users.IsInRoleAsync(user, role))
        {
            await users.AddToRoleAsync(user, role);
        }

        return user;
    }
}
