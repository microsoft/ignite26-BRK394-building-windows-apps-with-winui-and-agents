namespace Hangar.Services;

/// <summary>
/// Picks Ask Hangar's on-device model. Tries each <see cref="IAskModel"/> in order on a background
/// task (Aion Instruct preview when compiled in → Phi Silica) and reports honestly which one is
/// live. When none is, the pane streams <see cref="ScriptedResponder"/> answers instead.
/// Never throws to the UI.
/// </summary>
public sealed class LocalModelService
{
    public const string LafFeatureId = PhiSilicaModel.LafFeatureId;
    public const string LocalConfigFileName = PhiSilicaModel.LocalConfigFileName;

    public static LocalModelService Instance { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IAskModel? _active;
    private string? _preparing;
    private Task? _initTask;
    private bool _forceScripted;

    private LocalModelService()
    {
    }

    public event EventHandler? ModeChanged;

    /// <summary>True when an on-device model is loaded and scripted mode is not forced.</summary>
    public bool IsLive => _active is not null && !_forceScripted;

    public bool IsInitializing { get; private set; }

    public double InitProgress { get; private set; }

    public string FallbackReason { get; private set; } = "Checking for an on-device model on this PC…";

    /// <summary>Name of the live model, e.g. "Aion Instruct (preview)"; null in scripted mode.</summary>
    public string? ActiveModelName => IsLive ? _active!.DisplayName : null;

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

    public string ModeLabel => IsLive ? _active!.BadgeLabel : IsInitializing ? "Warming up the NPU…" : "Demo responses";

    public string ModeTooltip => IsLive
        ? _active!.LiveTooltip
        : IsInitializing
            ? $"Loading {_preparing ?? "the on-device model"} on the NPU. The first launch after install compiles the model and can take a few minutes; answers are scripted until it's ready."
            : _forceScripted
                ? "Scripted mode forced for stage (Ctrl+Shift+A). Deterministic answers, no model."
                : $"Scripted, deterministic answers. {FallbackReason}";

    public string FooterText => IsLive
        ? _active!.FooterText
        : "Scripted demo answers · grounded in this app's data";

    public Task InitializeAsync() => _initTask ??= Task.Run(InitializeCoreAsync);

    /// <summary>Waits (bounded) for a model that is still warming up, so an early question can go live.</summary>
    public async Task WaitForInitializationAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (_initTask is { IsCompleted: false } init)
        {
            try
            {
                await init.WaitAsync(timeout, ct);
            }
            catch (TimeoutException)
            {
            }
        }
    }

    private static IEnumerable<IAskModel> CreateBackends()
    {
#if HANGAR_AION
        yield return new AionModel();
#endif
        yield return new PhiSilicaModel();
    }

    private async Task InitializeCoreAsync()
    {
        IsInitializing = true;
        RaiseModeChanged();
        var reasons = new List<string>();
        try
        {
            foreach (var backend in CreateBackends())
            {
                _preparing = backend.DisplayName;
                InitProgress = 0;
                RaiseModeChanged();

                var reason = await backend.InitializeAsync(p =>
                {
                    InitProgress = p;
                    RaiseModeChanged();
                });
                if (reason is null)
                {
                    _active = backend;
                    FallbackReason = string.Empty;
                    AskLog.Write($"Live backend: {backend.DisplayName}");
                    return;
                }

                AskLog.Write($"{backend.DisplayName} unavailable: {reason}");
                reasons.Add(reason);
                backend.Dispose();
            }

            FallbackReason = string.Join(" ", reasons);
        }
        catch (Exception ex)
        {
            FallbackReason = $"On-device model couldn't start ({AskLog.Short(ex)}).";
            AskLog.Write($"Init threw {ex}");
        }
        finally
        {
            _preparing = null;
            IsInitializing = false;
            RaiseModeChanged();
        }
    }

    /// <summary>
    /// Streams a response from the live model. Returns null on success; otherwise a reason the
    /// caller should fall back to the scripted responder (moderation, context overflow, errors…).
    /// </summary>
    public async Task<string?> StreamAsync(string systemPrompt, string userPrompt, Action<string> onToken, CancellationToken ct)
    {
        var model = _active;
        if (model is null || _forceScripted)
        {
            return FallbackReason;
        }

        // One generation at a time on the NPU; a newer question cancels the older one first.
        try
        {
            await _gate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            return await model.StreamAsync(systemPrompt, userPrompt, onToken, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RaiseModeChanged() => ModeChanged?.Invoke(this, EventArgs.Empty);
}
