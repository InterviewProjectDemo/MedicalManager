using Microsoft.AspNetCore.Identity;

namespace MedicalManager.Data;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

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
