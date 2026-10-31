namespace MedicalManager.Data.Notifications;

/// <summary>
/// Spoken fallback when a family call is already in the carrier mailbox and the
/// person using the app does not talk. The clock here is the same rule the
/// browser uses on the raw microphone: silence accumulates only while speech
/// energy can be measured, meaningful speech resets it, and the dialog closes
/// only after the sentence has been played into the call.
/// </summary>
public static class VoicemailSilence
{
    public const string DialingPhase = "dialing";
    public const string GreetingPhase = "greeting";
    public const string LivePhase = "live";
    public const string LeavePhase = "leave-voicemail";
    public const string FinishedPhase = "finished";

    public const int SilenceMilliseconds = 30_000;

    /// <summary>
    /// RMS of the microphone time-domain samples. Room noise is usually under
    /// this; sustained speech is above it.
    /// </summary>
    public const double SpeechRms = 0.025;

    /// <summary>
    /// How long speech has to stay above <see cref="SpeechRms"/> before it
    /// counts as the person talking and the silence clock resets.
    /// </summary>
    public const int SpeechHoldMilliseconds = 300;

    public static void SelfCheck()
    {
        if (Sentence("Alex Rivera") != "Please call Alex Rivera. It is important.")
            throw new InvalidOperationException("Voicemail fallback must speak the signed-in user's full name.");
        if (Sentence("  Alex\tRivera ") != "Please call Alex Rivera. It is important.")
            throw new InvalidOperationException("Voicemail fallback should collapse the full name to plain words.");
        if (Sentence(null) is not null || Sentence("   ") is not null)
            throw new InvalidOperationException("Voicemail fallback needs a full name before it speaks.");

        if (PhaseFromAnsweredBy("machine_end_beep") != LeavePhase
            || PhaseFromAnsweredBy("machine_end_silence") != LeavePhase
            || PhaseFromAnsweredBy("machine_end_other") != LeavePhase)
            throw new InvalidOperationException("The mailbox is ready only after the carrier greeting ends.");
        if (PhaseFromAnsweredBy("machine_start") != GreetingPhase)
            throw new InvalidOperationException("A greeting that is still playing is not the leave-voicemail phase.");
        if (PhaseFromAnsweredBy("human") != LivePhase
            || PhaseFromAnsweredBy("unknown") != LivePhase
            || PhaseFromAnsweredBy("fax") != LivePhase)
            throw new InvalidOperationException("A live or uncertain answer must not start the voicemail fallback.");
        if (PhaseFromAnsweredBy(null) != DialingPhase || PhaseFromAnsweredBy("ringing") != DialingPhase)
            throw new InvalidOperationException("Ringing is not the leave-voicemail phase.");

        foreach (var phase in new[] { DialingPhase, GreetingPhase, LivePhase, FinishedPhase, "ringing", "", null })
        {
            if (ShouldArm(phase))
                throw new InvalidOperationException("The silence fallback armed outside leave-voicemail.");
        }

        if (!ShouldArm(LeavePhase) || !ShouldArm(PhaseFromAnsweredBy("machine_end_beep")))
            throw new InvalidOperationException("The silence fallback should arm when the mailbox is ready.");

        if (ResolvePhase(GreetingPhase, LeavePhase) != LeavePhase)
            throw new InvalidOperationException("The greeting ending should enter leave-voicemail.");
        if (ResolvePhase(LivePhase, LeavePhase) != LivePhase)
            throw new InvalidOperationException("A live conversation must not become leave-voicemail.");
        if (ResolvePhase(null, LeavePhase) != LeavePhase)
            throw new InvalidOperationException("The first mailbox-ready result should be kept.");

        var attrs = MachineDetectionAttributes("https://example.test/voice/family/amd?ticket=abc");
        if (!attrs.Contains("machineDetection=\"DetectMessageEnd\"", StringComparison.Ordinal)
            || attrs.Contains("machineDetection=\"Enable\"", StringComparison.Ordinal))
            throw new InvalidOperationException("Voicemail detection must wait until the greeting ends.");

        var quiet = default(SilenceClock);
        quiet = Advance(quiet, 0.001, 20_000, hearing: true);
        quiet = Advance(quiet, 0.08, 200, hearing: true);
        if (quiet.SilenceMs != 20_000 || quiet.Leaving)
            throw new InvalidOperationException("A short noise spike must not reset the voicemail silence clock.");
        quiet = Advance(quiet, 0.001, 9_999, hearing: true);
        if (quiet.Leaving || quiet.Closed)
            throw new InvalidOperationException("Twenty-nine seconds of silence must not leave the message yet.");
        quiet = Advance(quiet, 0.001, 1, hearing: true);
        if (!quiet.Leaving || quiet.Closed)
            throw new InvalidOperationException("Thirty seconds of silence should leave the message and keep the dialog open.");

        var talking = default(SilenceClock);
        talking = Advance(talking, 0.001, 20_000, hearing: true);
        talking = Advance(talking, SpeechRms, SpeechHoldMilliseconds, hearing: true);
        if (talking.SilenceMs != 0 || talking.Leaving)
            throw new InvalidOperationException("Meaningful speech must reset the voicemail silence clock.");
        talking = Advance(talking, 0.001, SilenceMilliseconds - 1, hearing: true);
        if (talking.Leaving)
            throw new InvalidOperationException("Speech should require a fresh 30 seconds of silence.");
        talking = Advance(talking, 0.001, 1, hearing: true);
        if (!talking.Leaving)
            throw new InvalidOperationException("Silence after speech should still leave the message.");

        var deaf = Advance(default, 0.0, 60_000, hearing: false);
        if (deaf.SilenceMs != 0 || deaf.Leaving)
            throw new InvalidOperationException("Silence must not accumulate when microphone energy cannot be measured.");

        var failed = MarkSpoken(quiet, played: false);
        if (failed.Closed || failed.Leaving || failed.SilenceMs != 0)
            throw new InvalidOperationException("A sentence that was not played must not close the dialog.");
        failed = Advance(failed, 0.001, SilenceMilliseconds, hearing: true);
        var left = MarkSpoken(failed, played: true);
        if (!left.Played || !left.Closed)
            throw new InvalidOperationException("The dialog closes only after the sentence has been left.");
    }

    public static string? Sentence(string? fullName)
    {
        var name = CleanName(fullName);
        return name is null ? null : $"Please call {name}. It is important.";
    }

    public static bool ShouldArm(string? phase) =>
        string.Equals(phase, LeavePhase, StringComparison.Ordinal);

    public static string PhaseFromAnsweredBy(string? answeredBy)
    {
        var value = answeredBy?.Trim().ToLowerInvariant() ?? "";
        return value switch
        {
            "machine_end_beep" or "machine_end_silence" or "machine_end_other" => LeavePhase,
            "machine_start" => GreetingPhase,
            "human" or "unknown" or "fax" => LivePhase,
            _ => DialingPhase
        };
    }

    public static string ResolvePhase(string? current, string incoming)
    {
        if (current is LeavePhase or LivePhase or FinishedPhase)
            return current;
        if (string.Equals(current, GreetingPhase, StringComparison.Ordinal)
            && !string.Equals(incoming, LeavePhase, StringComparison.Ordinal))
            return GreetingPhase;
        return incoming;
    }

    public static string MachineDetectionAttributes(string escapedCallbackUrl) =>
        $"machineDetection=\"DetectMessageEnd\" machineDetectionTimeout=\"45\" amdStatusCallback=\"{escapedCallbackUrl}\" amdStatusCallbackMethod=\"POST\"";

    public static SilenceClock Advance(SilenceClock state, double? rms, int elapsedMs, bool hearing)
    {
        if (state.Closed || state.Leaving)
            return state;
        if (!hearing || rms is null || elapsedMs <= 0)
            return state;
        if (rms.Value >= SpeechRms)
        {
            var hold = state.SpeechHoldMs + elapsedMs;
            if (hold >= SpeechHoldMilliseconds)
                return state with { SpeechHoldMs = hold, SilenceMs = 0 };
            return state with { SpeechHoldMs = hold };
        }

        var silence = state.SilenceMs + elapsedMs;
        return state with
        {
            SpeechHoldMs = 0,
            SilenceMs = silence,
            Leaving = silence >= SilenceMilliseconds
        };
    }

    public static SilenceClock MarkSpoken(SilenceClock state, bool played) =>
        played
            ? state with { Leaving = false, Played = true, Closed = true }
            : default;

    private static string? CleanName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        var builder = new System.Text.StringBuilder(fullName.Length);
        foreach (var ch in fullName.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                builder.Append(' ');
                continue;
            }

            if (char.IsControl(ch))
                continue;
            builder.Append(ch);
        }

        var collapsed = string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length == 0)
            return null;
        if (collapsed.Length > 120)
            collapsed = collapsed[..120].TrimEnd();
        return collapsed;
    }

    public readonly record struct SilenceClock(int SilenceMs, int SpeechHoldMs, bool Leaving, bool Played, bool Closed);
}
