using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hangar.Services;

/// <summary>A scripted (or live) answer: display text may carry trusted chip markers.</summary>
public sealed record AskAnswer(string Text, bool IsLogbookDraft = false, string? DraftText = null);

/// <summary>
/// Deterministic fallback for Ask Hangar. Streams word-sized tokens at a realistic 25–40 ms cadence
/// so the stage beat looks identical whether or not an on-device model is available.
/// </summary>
public static partial class ScriptedResponder
{
    public const string MayaShorthand = "bl 14 LE nick 0.4mm wl AMM 72-21 blend";

    [GeneratedRegex(@"\S+\s*|\s+")]
    private static partial Regex TokenRegex();

    public static AskAnswer Answer(string question, string page)
    {
        var intent = AskContextBuilder.DetectIntent(question, page);
        var data = FleetDataService.Instance;
        var hero = FleetDataService.HeroFlight;
        var leg = data.Legs[FleetDataService.HailFlightIndex];
        var peak = data.Vibration[^1];
        var hail = data.Vibration[FleetDataService.HailFlightIndex];
        var limit = FleetDataService.VibrationThreshold;

        return intent switch
        {
            AskIntent.WhyHeld => new AskAnswer(
                $"{hero} is held for Engine 2 N1 vibration. Readings sat near 0.9 ips until flight {FleetDataService.HailFlight} " +
                $"({leg.From}→{leg.To}), when the crew reported a hail encounter on climb at FL190. Vibration stepped to {hail:0.00} ips " +
                $"and has climbed to {peak:0.00} ips by flight 50, above the {limit:0.0} ips limit. That pattern points to hail/FOD damage " +
                "to the fan. AMM 72-00 requires a borescope before release.\n" +
                "[[flight:44]] [[part:fanblade14]] [[nav:inspections]]"),

            AskIntent.SeenBefore => new AskAnswer(
                $"Yes. Last year {FleetDataService.RelatedFlight} ({data.RelatedTail}) had the same fault: an Engine 2 fan blade 14 " +
                "leading-edge nick, blended per AMM 72-21 and returned to service. On this tail, " +
                $"WO-{FleetDataService.PriorHeroFanWorkOrder} found and blended a blade 14 nick 11 months ago, and " +
                $"WO-{FleetDataService.OpenHeroFanWorkOrder} is open now for the vibration exceedance. Expect a blend, not an engine change.\n" +
                $"[[wo:{FleetDataService.OpenHeroFanWorkOrder}]] [[wo:{FleetDataService.PriorHeroFanWorkOrder}]] " +
                (data.RelatedWorkOrderNumber > 0 ? $"[[wo:{data.RelatedWorkOrderNumber}]] " : string.Empty) +
                "[[part:fanblade14]]"),

            AskIntent.Logbook => LogbookAnswer(),

            AskIntent.WhatNext => new AskAnswer(
                $"Next for {hero}: complete the AMM 72-00 borescope on Engine 2, measure the blade 14 leading-edge nick, and blend per " +
                $"AMM 72-21 if within limits. Then run up and confirm N1 vibration is back below {limit:0.0} ips before release at " +
                $"{FleetDataService.Departure:HH:mm}.\n[[nav:inspections]] [[part:fanblade14]]"),

            _ => new AskAnswer(
                $"I can help with {hero}'s Engine 2 hold: the flight {FleetDataService.HailFlight} hail encounter, the vibration trend " +
                $"against the {limit:0.0} ips limit, related work orders, and drafting the logbook entry. Try one of the suggestions below.\n" +
                "[[flight:44]] [[part:fanblade14]]"),
        };
    }

    public static AskAnswer LogbookAnswer()
    {
        var draft = LogbookDraft();
        return new AskAnswer(
            $"Drafted from your note \"{MayaShorthand}\". Review before signing.\n[[part:fanblade14]] [[nav:inspections]]",
            IsLogbookDraft: true,
            DraftText: draft);
    }

    public static string LogbookDraft() =>
        $"{FleetDataService.HeroTail} Eng 2: borescope per AMM 72-00 found fan blade 14 leading-edge nick, 0.4 mm deep, " +
        "within limits. Blended per AMM 72-21. Post-blend inspection satisfactory. Engine run, N1 vibration within limits. " +
        "Aircraft returned to service.";

    /// <summary>Streams <paramref name="text"/> word by word; honours cancellation.</summary>
    public static async Task StreamAsync(string text, Action<string> onToken, CancellationToken ct)
    {
        var rng = new Random(text.Length);
        foreach (Match m in TokenRegex().Matches(text))
        {
            ct.ThrowIfCancellationRequested();
            onToken(m.Value);
            await Task.Delay(rng.Next(25, 41), ct);
        }
    }
}
