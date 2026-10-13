using System.Globalization;
using System.Linq;
using System.Text;
using Hangar.Models;
#if !HANGAR_REACTOR
using Hangar.Views;
#endif

namespace Hangar.Services;

public enum AskIntent { General, WhyHeld, SeenBefore, Logbook, WhatNext }

/// <summary>
/// Builds a compact grounding block for the on-device model from real in-app data, and the page-aware
/// suggestion chips. Kept well inside the on-device model's context window (~1.2k chars).
/// </summary>
public static class AskContextBuilder
{
    public const string AircraftQuestion = "Why is 2207 held, and what changed?";
    public const string WorkOrdersQuestion = "Have we seen this before?";
    public const string InspectionsQuestion = "Draft the logbook entry";
    public const string WhatNextQuestion = "What do I need to do before 06:15?";

#if !HANGAR_REACTOR
    public static string PageName(Type? page) => page switch
    {
        _ when page == typeof(AircraftPage) => "Aircraft",
        _ when page == typeof(WorkOrdersPage) => "WorkOrders",
        _ when page == typeof(InspectionsPage) => "Inspections",
        _ => "Fleet",
    };
#endif

    public static AskIntent DetectIntent(string question, string page)
    {
        var q = question.ToLowerInvariant();
        if (ContainsAny(q, "logbook", "draft", "entry", "sign-off", "sign off", "release")) return AskIntent.Logbook;
        if (ContainsAny(q, "seen", "before", "history", "similar", "last year", "1916")) return AskIntent.SeenBefore;
        if (ContainsAny(q, "next", "todo", "to do", "06:15", "need to do")) return AskIntent.WhatNext;
        if (ContainsAny(q, "held", "hold", "changed", "why", "vibration", "grounded", "2207")) return AskIntent.WhyHeld;
        return AskIntent.General;
    }

    public static IReadOnlyList<string> Suggestions(string page) => page switch
    {
        "Aircraft" => [AircraftQuestion, WhatNextQuestion],
        "WorkOrders" => [WorkOrdersQuestion, AircraftQuestion],
        "Inspections" => [InspectionsQuestion, WhatNextQuestion],
        _ => [AircraftQuestion, WorkOrdersQuestion],
    };

    /// <summary>System instructions + grounding + the question, as one prompt string.</summary>
    public static string BuildPrompt(string question, string page) =>
        BuildSystemPrompt(page) + BuildUserPrompt(question, page);

    /// <summary>
    /// Stable instructions and grounding FACTS. Aion Instruct takes this through
    /// CreateContext(systemPrompt); Phi Silica gets it prepended to the user turn.
    /// </summary>
    public static string BuildSystemPrompt(string page)
    {
        var data = FleetDataService.Instance;
        var inv = CultureInfo.InvariantCulture;
        var leg = data.Legs[FleetDataService.HailFlightIndex];
        var vib = data.Vibration;
        var baseline = vib.Take(FleetDataService.HailFlightIndex).Average();
        var hero = data.Hero;

        var sb = new StringBuilder(1400);
        sb.AppendLine("You are Ask Hangar, an offline assistant for airline maintenance engineers. Answer only from the FACTS. " +
                      "Be precise and concise: at most 90 words, plain sentences, no lists, no markdown, no speculation beyond the facts. " +
                      "Never invent work order numbers; only cite WO numbers that appear in the FACTS.");
        sb.AppendLine("FACTS:");
        sb.AppendLine(string.Create(inv, $"- Time {FleetDataService.Now:HH:mm}, user Maya Okafor (night-shift engineer), screen: {page}."));
        sb.AppendLine(string.Create(inv,
            $"- Aircraft {FleetDataService.HeroTail} ({FleetDataService.HeroFlight}, {hero.Type}) status {hero.Status}: held for Engine 2 N1 vibration. Departure {FleetDataService.Departure:HH:mm}."));
        sb.AppendLine(string.Create(inv,
            $"- Engine 2 N1 vibration: normal (~{baseline:0.0} ips) through flight {FleetDataService.HailFlight - 1}. " +
            $"Flight {FleetDataService.HailFlight} {leg.From}-{leg.To}: crew reported hail encounter on climb at FL190; vibration stepped to {vib[FleetDataService.HailFlightIndex]:0.00} ips. " +
            $"Climbed to {vib[^1]:0.00} ips by flight {vib.Count}. Limit {FleetDataService.VibrationThreshold:0.0} ips."));
        sb.AppendLine("- Likely cause: hail/FOD damage to the Engine 2 fan (blade 14 leading edge).");

        var open = data.WorkOrders
            .Where(w => w.Tail == FleetDataService.HeroTail && w.Status != WorkOrderStatus.Closed && w.Ata.StartsWith("72", StringComparison.Ordinal))
            .Take(3)
            .Select(w => $"{w.NumberText} {w.Component} ({w.StatusText}): {w.Finding}");
        sb.AppendLine("- Open Engine 2 work orders: " + string.Join("; ", open) + ".");
        sb.AppendLine("- AMM 72-00: borescope inspection required before release. AMM 72-21: fan blade blend repair limits and procedure.");
        sb.AppendLine(
            "- History: WO-" + FleetDataService.PriorHeroFanWorkOrder.ToString(inv) + " (this tail, 11 months ago) blade 14 nick blended. " +
            "Last year " + FleetDataService.RelatedFlight + " (" + data.RelatedTail + ") had the same Engine 2 fan blade 14 leading-edge nick" +
            (data.RelatedWorkOrderNumber > 0 ? " (WO-" + data.RelatedWorkOrderNumber.ToString(inv) + ")" : string.Empty) +
            ", blend repair per AMM 72-21, returned to service.");
        return sb.ToString();
    }

    /// <summary>
    /// The user turn: the task for this intent, the question quoted as data, and the
    /// answer-only instruction last (per the Aion prompt guide; also fine for Phi Silica).
    /// </summary>
    public static string BuildUserPrompt(string question, string page)
    {
        var data = FleetDataService.Instance;
        var inv = CultureInfo.InvariantCulture;
        var leg = data.Legs[FleetDataService.HailFlightIndex];
        var vib = data.Vibration;
        var baseline = vib.Take(FleetDataService.HailFlightIndex).Average();
        var intent = DetectIntent(question, page);

        var sb = new StringBuilder(600);
        if (intent == AskIntent.Logbook)
        {
            sb.AppendLine($"- Maya's shorthand note: \"{ScriptedResponder.MayaShorthand}\" (bl=blade, LE=leading edge, wl=within limits).");
            sb.AppendLine("TASK: Expand the note into one compliant logbook release entry: tail, engine, inspection reference AMM 72-00, finding with measurement, " +
                          "within limits, repair per AMM 72-21, post-repair inspection and engine run satisfactory, returned to service. Output only the entry. Do not sign it.");
        }
        else if (intent == AskIntent.WhyHeld)
        {
            sb.AppendLine(string.Create(inv,
                $"TASK: In 2-3 sentences say: it is held because Engine 2 vibration {vib[^1]:0.00} ips exceeds the {FleetDataService.VibrationThreshold:0.0} ips limit; " +
                $"what changed is flight {FleetDataService.HailFlight} ({leg.From}-{leg.To}) hail encounter on climb at FL190, vibration stepped from {baseline:0.0} to {vib[FleetDataService.HailFlightIndex]:0.00} ips and climbed to {vib[^1]:0.00}; " +
                $"likely hail/FOD damage to fan blade 14; an AMM 72-00 borescope is required before release."));
        }
        else if (intent == AskIntent.SeenBefore)
        {
            sb.AppendLine(string.Create(inv,
                $"TASK: In 2-3 sentences say yes: last year {FleetDataService.RelatedFlight} ({data.RelatedTail}) had the same Engine 2 fan blade 14 leading-edge nick, " +
                $"blended per AMM 72-21 and returned to service; this tail also had WO-{FleetDataService.PriorHeroFanWorkOrder} (blade 14 blend) and now WO-{FleetDataService.OpenHeroFanWorkOrder} is open."));
        }

        sb.AppendLine("QUESTION: \"" + question.Trim().Replace("\"", "'", StringComparison.Ordinal) + "\"");
        sb.Append("Answer the QUESTION using only the FACTS, in plain sentences, at most 90 words. Output only the answer.");
        return sb.ToString();
    }

    private static bool ContainsAny(string s, params string[] keys) => keys.Any(k => s.Contains(k, StringComparison.Ordinal));
}
