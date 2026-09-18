using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public class UserDashboardLayout
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Required]
    public string LayoutJson { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
