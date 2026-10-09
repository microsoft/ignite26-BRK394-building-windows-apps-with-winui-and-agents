using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Text;
using Windows.ApplicationModel;
using Windows.Storage;

namespace Hangar.Services;

/// <summary>
/// Wraps Phi Silica (<see cref="LanguageModel"/>) on the NPU. Initializes on a background task,
/// reports honestly whether it is live, and never throws to the UI — every failure path leaves
/// <see cref="IsLive"/> false with a human-readable <see cref="FallbackReason"/>.
/// </summary>
public sealed class LocalModelService
{
    public const string LafFeatureId = "com.microsoft.windows.ai.languagemodel";
    public const string LocalConfigFileName = "ask-hangar.local.json";

    public static LocalModelService Instance { get; } = new();

    private const int MaxPromptChars = 12000;
    private LanguageModel? _model;
    private Task? _initTask;
    private bool _forceScripted;

    private LocalModelService()
    {
    }

    public event EventHandler? ModeChanged;

    /// <summary>True when Phi Silica is created and usable and scripted mode is not forced.</summary>
    public bool IsLive => _model is not null && !_forceScripted;

    public bool IsInitializing { get; private set; }

    public double InitProgress { get; private set; }

    public string FallbackReason { get; private set; } = "Checking for Phi Silica on this PC…";

    public bool ForceScripted
    {
        get => _forceScripted;
        set
        {
            if (_forceScripted == value)
            {
                return;
            }

            _forceScripted = value;
            RaiseModeChanged();
        }
    }

    public string ModeLabel => IsLive ? "On-device · Phi Silica · NPU" : IsInitializing ? "Preparing Phi Silica…" : "Demo responses";

    public string ModeTooltip => IsLive
        ? "Answers are generated locally by Phi Silica on the Hexagon NPU. Nothing leaves this PC."
        : _forceScripted
            ? "Scripted mode forced for stage (Ctrl+Shift+A). Deterministic answers, no model."
            : $"Scripted, deterministic answers. {FallbackReason}";

    public Task InitializeAsync() => _initTask ??= Task.Run(InitializeCoreAsync);

    private async Task InitializeCoreAsync()
    {
        IsInitializing = true;
        RaiseModeChanged();
        try
        {
            TryUnlockLimitedAccessFeature();

            var state = LanguageModel.GetReadyState();
            Log($"GetReadyState = {state}");
            switch (state)
            {
                case AIFeatureReadyState.NotSupportedOnCurrentSystem:
                case AIFeatureReadyState.NotCompatibleWithSystemHardware:
                    Fail("Phi Silica isn't supported on this device.");
                    return;
                case AIFeatureReadyState.DisabledByUser:
                    Fail("On-device AI is turned off in Windows settings.");
                    return;
                case AIFeatureReadyState.CapabilityMissing:
                    Fail("The app is missing the systemAIModels capability.");
                    return;
                case AIFeatureReadyState.OSUpdateNeeded:
                    Fail("A Windows update is needed for Phi Silica.");
                    return;
                case AIFeatureReadyState.NotReady:
                    var op = LanguageModel.EnsureReadyAsync();
                    op.Progress = (_, p) =>
                    {
                        InitProgress = p;
                        RaiseModeChanged();
                    };
                    var ready = await op;
                    Log($"EnsureReadyAsync = {ready.Status} {ready.ErrorDisplayText} {ready.ExtendedError}");
                    if (ready.Status != AIFeatureReadyResultState.Success)
                    {
                        Fail(string.IsNullOrWhiteSpace(ready.ErrorDisplayText)
                            ? $"Phi Silica model isn't ready ({ready.ExtendedError?.Message ?? "unknown error"})."
                            : ready.ErrorDisplayText);
                        return;
                    }

                    break;
            }

            _model = await LanguageModel.CreateAsync();
            Log("LanguageModel.CreateAsync succeeded");
            FallbackReason = string.Empty;
        }
        catch (UnauthorizedAccessException ex)
        {
            Fail("Limited Access Feature is locked (no unlock token configured).", ex);
        }
        catch (Exception ex)
        {
            Fail($"Phi Silica couldn't start ({ShortError(ex)}).", ex);
        }
        finally
        {
            IsInitializing = false;
            RaiseModeChanged();
        }
    }

    /// <summary>
    /// Streams a response. Returns null on success; otherwise a reason the caller should fall back
    /// to the scripted responder (moderation, context overflow, errors…).
    /// </summary>
    public async Task<string?> StreamAsync(string prompt, Action<string> onToken, CancellationToken ct)
    {
        if (!IsLive || _model is null)
        {
            return FallbackReason;
        }

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
                    onToken(delta);
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
            Log($"GenerateResponseAsync = {result.Status}");
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
            Log($"GenerateResponseAsync threw {ex}");
            return $"Phi Silica error ({ShortError(ex)}).";
        }
    }

    private void TryUnlockLimitedAccessFeature()
    {
        try
        {
            var (token, attestation) = ReadLafCredentials();
            if (string.IsNullOrWhiteSpace(token))
            {
                Log("No LAF token configured; relying on the OS policy for this package.");
                return;
            }

            var result = LimitedAccessFeatures.TryUnlockFeature(LafFeatureId, token, attestation ?? string.Empty);
            Log($"TryUnlockFeature = {result.Status}");
        }
        catch (Exception ex)
        {
            Log($"TryUnlockFeature threw {ex.Message}");
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

    private static System.Collections.Generic.IEnumerable<string> CandidateConfigDirectories()
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

    private void Fail(string reason, Exception? ex = null)
    {
        FallbackReason = reason;
        _model?.Dispose();
        _model = null;
        Log($"Fallback: {reason} {ex}");
    }

    private static string ShortError(Exception ex) => $"0x{ex.HResult:X8}";

    private void RaiseModeChanged() => ModeChanged?.Invoke(this, EventArgs.Empty);

    private static void Log(string message) => System.Diagnostics.Debug.WriteLine($"[AskHangar] {message}");
}
