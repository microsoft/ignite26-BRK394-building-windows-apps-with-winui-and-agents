#if HANGAR_AION
using System.Diagnostics;
using System.Text;
using AionInstructPreview.Text;

namespace Hangar.Services;

/// <summary>
/// Aion Instruct developer preview (aka.ms/tryaion), the on-device successor to Phi Silica.
/// Compiled only when the git-ignored nuget-local/ feed holds the SDK nupkg (EnableAion).
/// The API mirrors Microsoft.Windows.AI.Text.LanguageModel, but progress reports deltas and the
/// system prompt goes through <see cref="LanguageModel.CreateContext(string)"/>.
/// Developer validation only: output is watermarked and non-deterministic.
/// </summary>
public sealed class AionModel : IAskModel
{
    private const int EFail = unchecked((int)0x80004005);
    private LanguageModel? _model;

    public string DisplayName => "Aion Instruct (preview)";

    public string BadgeLabel => "On-device · Aion Instruct (preview) · NPU";

    public string LiveTooltip =>
        "Answers are generated locally by the Aion Instruct developer preview on the Hexagon NPU. " +
        "Preview output is watermarked and can vary between runs. Nothing leaves this PC.";

    public string FooterText => "Aion Instruct (preview) · on this PC's NPU · offline";

    public async Task<string?> InitializeAsync(Action<double> onProgress)
    {
        try
        {
            // First launch after install compiles the QNN graphs for the NPU; that can take minutes.
            var sw = Stopwatch.StartNew();
            _model = await LanguageModel.CreateAsync();
            AskLog.Write($"Aion CreateAsync succeeded in {sw.ElapsedMilliseconds} ms");

            // The first generation after a cold compile can fail with E_FAIL; prime it here so the
            // first real question streams immediately.
            sw.Restart();
            var warm = await StreamAsync("You are a test harness.", "Reply with the single word OK.", _ => { }, CancellationToken.None);
            AskLog.Write($"Aion warm-up = {warm ?? "OK"} in {sw.ElapsedMilliseconds} ms");
            if (warm is not null)
            {
                Dispose();
                return warm;
            }

            return null;
        }
        catch (Exception ex) when (ex is TypeLoadException or System.Runtime.InteropServices.COMException { HResult: unchecked((int)0x80040154) })
        {
            AskLog.Write($"Aion not installed: {ex}");
            Dispose();
            return "The Aion Instruct preview framework isn't installed (run the sample's Bootstrap.ps1).";
        }
        catch (Exception ex)
        {
            AskLog.Write($"Aion init threw {ex}");
            Dispose();
            return $"Aion Instruct couldn't start ({AskLog.Short(ex)}).";
        }
    }

    public async Task<string?> StreamAsync(string systemPrompt, string userPrompt, Action<string> onDelta, CancellationToken ct)
    {
        if (_model is null)
        {
            return "Aion Instruct isn't loaded.";
        }

        for (var attempt = 0; ; attempt++)
        {
            var gotDelta = false;
            try
            {
                using var context = _model.CreateContext(systemPrompt);
                var options = new LanguageModelOptions { Temperature = 0.2f, TopK = 20, TopP = 0.9f };
                var op = _model.GenerateResponseAsync(context, userPrompt, options);
                op.Progress = (_, delta) =>
                {
                    if (!ct.IsCancellationRequested && !string.IsNullOrEmpty(delta))
                    {
                        gotDelta = true;
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
                AskLog.Write($"Aion GenerateResponseAsync = {result.Status}");
                return result.Status switch
                {
                    LanguageModelResponseStatus.Complete => null,
                    LanguageModelResponseStatus.PromptBlockedByContentModeration or
                    LanguageModelResponseStatus.ResponseBlockedByContentModeration => "Content moderation blocked the answer.",
                    LanguageModelResponseStatus.PromptLargerThanContext => "Prompt larger than Aion Instruct's context window.",
                    LanguageModelResponseStatus.BlockedByPolicy => "Blocked by device policy.",
                    _ => $"Aion Instruct returned {result.Status}.",
                };
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex) when (ex.HResult == EFail && !gotDelta && attempt == 0 && !ct.IsCancellationRequested)
            {
                AskLog.Write("Aion GenerateResponseAsync E_FAIL before first delta; retrying once.");
            }
            catch (Exception ex)
            {
                AskLog.Write($"Aion GenerateResponseAsync threw {ex}");
                return $"Aion Instruct error ({AskLog.Short(ex)}).";
            }
        }
    }

    public void Dispose()
    {
        _model?.Dispose();
        _model = null;
    }
}
#endif
