using Microsoft.AspNetCore.Identity;

namespace MedicalManager.Data;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>Clinical specialty for a doctor account. Empty for patients.</summary>
    public string? Specialty { get; set; }

    /// <summary>Street address of the doctor's clinic. Empty for patients.</summary>
    public string? ClinicAddress { get; set; }

    /// <summary>Medical school attended. Empty for patients.</summary>
    public string? MedicalSchool { get; set; }

    /// <summary>US state or DC where the doctor is licensed. Empty for patients.</summary>
    public string? LicenseState { get; set; }

    /// <summary>State license number. Empty for patients.</summary>
    public string? LicenseNumber { get; set; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(FullName) ? Email ?? "Patient" : FullName;

    public PatientProfile? Profile { get; set; }

    public ICollection<BloodPressureReading> BloodPressureReadings { get; set; } = [];
    public ICollection<SugarReading> SugarReadings { get; set; } = [];
    public ICollection<Medication> Medications { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<Doctor> Doctors { get; set; } = [];
    public ICollection<LabReport> LabReports { get; set; } = [];
    public ICollection<ToDoItem> ToDoItems { get; set; } = [];
    public UserDashboardLayout? DashboardLayout { get; set; }
}
