namespace MedicalManager.Data.Notifications;

public static class FamilyCallSignals
{
    public static void SelfCheck()
    {
        if (!IsTerminal("31005 ConnectionError (31005): Error sent from gateway in HANGUP"))
            throw new InvalidOperationException("A remote hangup should close the call dialog.");
        if (!IsTerminal("31008 Call cancelled"))
            throw new InvalidOperationException("A canceled call should close the call dialog.");
        if (!IsTerminal("31603 Decline"))
            throw new InvalidOperationException("A rejected call should close the call dialog.");
        if (!IsTerminal("no-answer"))
            throw new InvalidOperationException("No answer should close the call dialog.");
        if (IsTerminal("Allow the microphone so you can talk."))
            throw new InvalidOperationException("A microphone prompt is not the end of a call.");
        if (IsTerminal("The call could not be placed."))
            throw new InvalidOperationException("A setup failure should stay on screen.");
        if (!IsSetupFailure("Allow the microphone so you can talk."))
            throw new InvalidOperationException("A microphone prompt should stay on screen.");
        if (IsSetupFailure("31005 ConnectionError HANGUP"))
            throw new InvalidOperationException("A hangup must not be treated as a setup failure.");
        if (IsSetupFailure(null))
            throw new InvalidOperationException("A normal disconnect has no error and should close.");
    }

    public static bool IsTerminal(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return Contains(message, "hangup")
            || message.Contains("31005", StringComparison.Ordinal)
            || message.Contains("31008", StringComparison.Ordinal)
            || message.Contains("31003", StringComparison.Ordinal)
            || message.Contains("31603", StringComparison.Ordinal)
            || Contains(message, "no-answer")
            || Contains(message, "no answer")
            || Contains(message, "canceled")
            || Contains(message, "cancelled")
            || Contains(message, "busy")
            || Contains(message, "declined")
            || Contains(message, "rejected");
    }

    public static bool IsSetupFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || IsTerminal(message))
            return false;

        return Contains(message, "microphone")
            || Contains(message, "could not be placed")
            || Contains(message, "not available")
            || Contains(message, "10-digit")
            || Contains(message, "phone number");
    }

    private static bool Contains(string message, string value) =>
        message.Contains(value, StringComparison.OrdinalIgnoreCase);
}
