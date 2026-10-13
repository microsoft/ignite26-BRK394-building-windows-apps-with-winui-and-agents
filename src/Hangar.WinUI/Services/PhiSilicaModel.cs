using System.IO;
using System.Text.Json;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Text;
using Windows.ApplicationModel;
using Windows.Storage;

namespace Hangar.Services;

/// <summary>Phi Silica (<see cref="LanguageModel"/>, inbox on Copilot+ PCs) on the NPU.</summary>
public sealed class PhiSilicaModel : IAskModel
{
    public const string LafFeatureId = "com.microsoft.windows.ai.languagemodel";
    public const string LocalConfigFileName = "ask-hangar.local.json";

    private const int MaxPromptChars = 12000;
    private LanguageModel? _model;

    public string DisplayName => "Phi Silica";

    public string BadgeLabel => "On-device · Phi Silica · NPU";

    public string LiveTooltip => "Answers are generated locally by Phi Silica on the Hexagon NPU. Nothing leaves this PC.";

    public string FooterText => "Phi Silica on this PC's NPU · nothing leaves the device";

    public async Task<string?> InitializeAsync(Action<double> onProgress)
    {
        try
        {
            TryUnlockLimitedAccessFeature();

            var state = LanguageModel.GetReadyState();
            AskLog.Write($"Phi Silica GetReadyState = {state}");
            switch (state)
            {
                case AIFeatureReadyState.NotSupportedOnCurrentSystem:
                case AIFeatureReadyState.NotCompatibleWithSystemHardware:
                    return "Phi Silica isn't supported on this device.";
                case AIFeatureReadyState.DisabledByUser:
                    return "On-device AI is turned off in Windows settings.";
                case AIFeatureReadyState.CapabilityMissing:
                    return "The app is missing the systemAIModels capability.";
                case AIFeatureReadyState.OSUpdateNeeded:
                    return "A Windows update is needed for Phi Silica.";
                case AIFeatureReadyState.NotReady:
                    var op = LanguageModel.EnsureReadyAsync();
                    op.Progress = (_, p) => onProgress(p);
                    var ready = await op;
                    AskLog.Write($"Phi Silica EnsureReadyAsync = {ready.Status} {ready.ErrorDisplayText} {ready.ExtendedError}");
                    if (ready.Status != AIFeatureReadyResultState.Success)
                    {
                        return string.IsNullOrWhiteSpace(ready.ErrorDisplayText)
                            ? $"Phi Silica model isn't ready ({ready.ExtendedError?.Message ?? "unknown error"})."
                            : ready.ErrorDisplayText;
                    }

                    break;
            }

            _model = await LanguageModel.CreateAsync();
            AskLog.Write("Phi Silica LanguageModel.CreateAsync succeeded");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            AskLog.Write($"Phi Silica LAF locked: {ex}");
            return "Limited Access Feature is locked (no unlock token configured).";
        }
        catch (Exception ex)
        {
            AskLog.Write($"Phi Silica init threw {ex}");
            return $"Phi Silica couldn't start ({AskLog.Short(ex)}).";
        }
    }

    public async Task<string?> StreamAsync(string systemPrompt, string userPrompt, Action<string> onDelta, CancellationToken ct)
    {
        if (_model is null)
        {
            return "Phi Silica isn't loaded.";
        }

        // Phi Silica takes one prompt string: system instructions + facts, then the user turn.
        var prompt = systemPrompt + userPrompt;
        try
        {
            // GetUsablePromptLength (ILanguageModel2) access-violates in this AI package/OS build,
            // so budget by characters (~4 chars/token) and let PromptLargerThanContext catch the rest.
            if (prompt.Length > MaxPromptChars)
            {
                return "Prompt larger than Phi Silica's context window.";
            }

            var options = new LanguageModelOptions { Temperature = 0.2f, TopK = 20, TopP = 0.9f };
            var op = _model.GenerateResponseAsync(prompt, options);
            op.Progress = (_, delta) =>
            {
                if (!ct.IsCancellationRequested && !string.IsNullOrEmpty(delta))
                {
                    onDelta(delta);
                }
            };
            using var reg = ct.Register(() =>
            {
                try
                {
                    op.Cancel();
                }
                catch
                {
                }
            });

            var result = await op;
            AskLog.Write($"Phi Silica GenerateResponseAsync = {result.Status}");
            return result.Status switch
            {
                LanguageModelResponseStatus.Complete => null,
                LanguageModelResponseStatus.PromptBlockedByContentModeration or
                LanguageModelResponseStatus.ResponseBlockedByContentModeration => "Content moderation blocked the answer.",
                LanguageModelResponseStatus.PromptLargerThanContext => "Prompt larger than Phi Silica's context window.",
                LanguageModelResponseStatus.BlockedByPolicy => "Blocked by device policy.",
                LanguageModelResponseStatus.LowOnVram => "NPU memory is low.",
                LanguageModelResponseStatus.BlockedByGameMode => "Paused while Game Mode is active.",
                _ => $"Phi Silica returned {result.Status}.",
            };
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            AskLog.Write($"Phi Silica GenerateResponseAsync threw {ex}");
            return $"Phi Silica error ({AskLog.Short(ex)}).";
        }
    }

    public void Dispose()
    {
        _model?.Dispose();
        _model = null;
    }

    private static void TryUnlockLimitedAccessFeature()
    {
        try
        {
            var (token, attestation) = ReadLafCredentials();
            if (string.IsNullOrWhiteSpace(token))
            {
                AskLog.Write("No LAF token configured; relying on the OS policy for this package.");
                return;
            }

            var result = LimitedAccessFeatures.TryUnlockFeature(LafFeatureId, token, attestation ?? string.Empty);
            AskLog.Write($"TryUnlockFeature = {result.Status}");
        }
        catch (Exception ex)
        {
            AskLog.Write($"TryUnlockFeature threw {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the LAF token from env vars (HANGAR_LAF_TOKEN / HANGAR_LAF_ATTESTATION) or a
    /// git-ignored ask-hangar.local.json in LocalState or next to the exe. Never committed.
    /// </summary>
    private static (string? Token, string? Attestation) ReadLafCredentials()
    {
        var token = Environment.GetEnvironmentVariable("HANGAR_LAF_TOKEN");
        var attestation = Environment.GetEnvironmentVariable("HANGAR_LAF_ATTESTATION");
        if (!string.IsNullOrWhiteSpace(token))
        {
            return (token, attestation);
        }

        foreach (var dir in CandidateConfigDirectories())
        {
            var path = Path.Combine(dir, LocalConfigFileName);
            if (!File.Exists(path))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            return (
                root.TryGetProperty("lafToken", out var t) ? t.GetString() : null,
                root.TryGetProperty("lafAttestation", out var a) ? a.GetString() : null);
        }

        return (null, null);
    }

    private static IEnumerable<string> CandidateConfigDirectories()
    {
        string? local = null;
        try
        {
            local = ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
        }

        if (local is not null)
        {
            yield return local;
        }

        yield return AppContext.BaseDirectory;
    }
}
