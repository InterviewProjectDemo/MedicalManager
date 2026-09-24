using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public class PatientProfile
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    [MaxLength(40)]
    public string? Sex { get; set; }

    [MaxLength(40)]
    public string? Phone { get; set; }

    [MaxLength(300)]
    public string? HomeAddress { get; set; }

    /// <summary>
    /// How the reminder call should say this person's name, for example "nah-VEEN SHAR-mah".
    /// When empty, the call uses a pronunciation dictionary and then the spelling.
    /// </summary>
    [MaxLength(200)]
    public string? NamePronunciation { get; set; }

    public bool AppointmentRemindersEnabled { get; set; } = true;

    [MaxLength(400)]
    public string? Notes { get; set; }

    public byte[]? ProfilePhotoData { get; set; }

    [MaxLength(100)]
    public string? ProfilePhotoContentType { get; set; }

    public DateTime? ProfilePhotoUpdatedAt { get; set; }

    public bool HasCompletedOnboarding { get; set; }

    public DateTime? OnboardingCompletedAt { get; set; }
}
