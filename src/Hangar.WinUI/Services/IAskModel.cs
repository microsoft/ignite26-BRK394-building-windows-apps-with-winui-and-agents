namespace Hangar.Services;

/// <summary>
/// One on-device language model backend for Ask Hangar. <see cref="LocalModelService"/> tries
/// backends in order (Aion Instruct preview → Phi Silica) and falls back to
/// <see cref="ScriptedResponder"/> when none is live. Implementations never throw to callers.
/// </summary>
public interface IAskModel : IDisposable
{
    /// <summary>Short model name, e.g. "Phi Silica".</summary>
    string DisplayName { get; }

    /// <summary>Honest mode badge shown while this backend is live.</summary>
    string BadgeLabel { get; }

    string LiveTooltip { get; }

    string FooterText { get; }

    /// <summary>Loads the model. Returns null when live, otherwise a human-readable reason.</summary>
    Task<string?> InitializeAsync(Action<double> onProgress);

    /// <summary>
    /// Streams an answer. <paramref name="onDelta"/> receives new text only (never the running total).
    /// Returns null on success; otherwise a reason the caller should fall back to scripted answers.
    /// </summary>
    Task<string?> StreamAsync(string systemPrompt, string userPrompt, Action<string> onDelta, CancellationToken ct);
}
