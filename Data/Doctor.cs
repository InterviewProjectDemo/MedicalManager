using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public enum DoctorSource
{
    Manual,
    FromAppointment,
    FromMedication
}

public class Doctor
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Specialty { get; set; } = string.Empty;

    [MaxLength(40)]
    public string PhoneNumber { get; set; } = string.Empty;

    public DoctorSource Source { get; set; } = DoctorSource.Manual;

    public bool IsAutoDiscovered { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
