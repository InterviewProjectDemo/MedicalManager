namespace MedicalManager.Data;

/// <summary>
/// The one granted patient a doctor is working with on this signed-in circuit.
/// </summary>
public sealed class GrantedPatientSelection
{
    public string? PatientUserId { get; private set; }

    public string? PatientName { get; private set; }

    public event Action? Changed;

    public void Select(string? patientUserId, string? patientName)
    {
        var id = string.IsNullOrWhiteSpace(patientUserId) ? null : patientUserId.Trim();
        var name = string.IsNullOrWhiteSpace(patientName) ? null : patientName.Trim();
        var same = string.Equals(PatientUserId, id, StringComparison.Ordinal)
            && string.Equals(PatientName, name, StringComparison.Ordinal);
        PatientUserId = id;
        PatientName = name;
        if (!same)
            Changed?.Invoke();
    }
}
