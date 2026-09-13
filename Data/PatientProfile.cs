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

    [MaxLength(400)]
    public string? Notes { get; set; }

    public byte[]? ProfilePhotoData { get; set; }

    [MaxLength(100)]
    public string? ProfilePhotoContentType { get; set; }

    public DateTime? ProfilePhotoUpdatedAt { get; set; }
}
