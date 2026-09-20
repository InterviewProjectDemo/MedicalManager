namespace MedicalManager.Data;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string FromAddress { get; set; } = "medmgr.us@gmail.com";

    public string FromDisplayName { get; set; } = "Medical Manager";

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";

    public int Port { get; set; } = 587;

    public string? User { get; set; }

    public string? Password { get; set; }
}
