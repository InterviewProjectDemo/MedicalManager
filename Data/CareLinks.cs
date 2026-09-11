namespace MedicalManager.Data;

public static class CareLinks
{
    public static string To(string path, string? patientUserId)
    {
        var relative = string.IsNullOrWhiteSpace(path) || path == "/"
            ? ""
            : path.Trim('/');

        if (string.IsNullOrWhiteSpace(patientUserId))
        {
            return relative;
        }

        var query = $"patientUserId={Uri.EscapeDataString(patientUserId)}";
        return string.IsNullOrEmpty(relative)
            ? $"/?{query}"
            : $"{relative}?{query}";
    }
}
