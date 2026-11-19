namespace MedicalManager.Data;

/// <summary>
/// The one granted patient a doctor is working with on this signed-in circuit.
/// </summary>
public sealed class GrantedPatientSelection
{
    public string? PatientUserId { get; private set; }

    public string? PatientName { get; private set; }

    public void Select(string? patientUserId, string? patientName)
    {
        PatientUserId = string.IsNullOrWhiteSpace(patientUserId) ? null : patientUserId.Trim();
        PatientName = string.IsNullOrWhiteSpace(patientName) ? null : patientName.Trim();
    }
}
