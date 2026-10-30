using Daignostic.Core.Ai;

namespace Daignostic.Services;

/// <summary>The words the app uses when an AI request fails, the same everywhere an AI is asked.</summary>
internal static class AiErrorText
{
    public static (string Title, string Advice, bool NeedsSettings) Describe(AiException ex) => ex.Kind switch
    {
        AiErrorKind.Endpoint => ("Could not reach the AI", "Check that the service is running and that the address in Settings is right. A local gateway has to be started first.", true),
        AiErrorKind.Key => ("The AI did not accept your API key", "Open Settings and re-enter the key. Check that it is for this service and has not expired.", true),
        AiErrorKind.Model => ("The AI did not accept the address or the model name", "In Settings, press List models and pick a name from the list. The address works with or without /v1.", true),
        AiErrorKind.Timeout => ("The AI took too long", "Try again, or choose a faster model in Settings. Very long answers from large models can exceed the limit.", false),
        AiErrorKind.Reply => ("The AI answered, but not in a form daignostic can read", "The model did not follow the required format. Try again, or use a stronger model.", false),
        _ => ("The AI request failed", "Try again. If it keeps happening, check Settings.", false),
    };

    /// <summary>One line of progress for a request that is running, with the time so far and when daignostic gives up.</summary>
    public static string Waiting(TimeSpan elapsed, string model)
    {
        var left = Math.Max(0, AppServices.AiTimeoutSeconds - (int)elapsed.TotalSeconds);
        var clock = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        return elapsed.TotalSeconds < 1.5
            ? $"Packaging the file for {model}... {clock}"
            : $"Sent to {model}. Waiting for the answer... {clock}. daignostic gives up after {AppServices.AiTimeoutSeconds / 60} minutes ({left} s left)."
              + (elapsed.TotalSeconds >= 60 ? " This is taking unusually long; you can cancel and try a faster model." : elapsed.TotalSeconds >= 20 ? " Larger models can take a minute or more." : "");
    }
}
