using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public class MedicationSchedule
{
    public int Id { get; set; }

    [Required]
    public int MedicationId { get; set; }

    public Medication? Medication { get; set; }

    public MedicationTimeSlot TimeSlot { get; set; }
}
