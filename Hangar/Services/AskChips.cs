using System.Text.RegularExpressions;

namespace Hangar.Services;

public enum AskChipKind
{
    Flight,
    Part,
    WorkOrder,
    Nav,
}

/// <summary>An action chip appended to an Ask Hangar answer.</summary>
public sealed record AskChip(AskChipKind Kind, string Value, string Label, string Glyph);

/// <summary>
/// Parses the citation marker syntax used by Ask Hangar answers:
/// <c>[[flight:44]]</c>, <c>[[part:fanblade14]]</c>, <c>[[wo:46871]]</c>, <c>[[nav:inspections]]</c>.
/// Scripted answers carry trusted markers; live model output is never trusted — markers are stripped
/// and chips are derived deterministically from keywords instead.
/// </summary>
public static partial class AskChips
{
    [GeneratedRegex(@"\[\[\s*(flight|part|wo|nav)\s*:\s*([A-Za-z0-9\-]+)\s*\]\]", RegexOptions.IgnoreCase)]
    private static partial Regex MarkerRegex();

    [GeneratedRegex(@"\[\[[^\]]*\]?\]?")]
    private static partial Regex AnyMarkerRegex();

    [GeneratedRegex(@"\bWO-?(\d{5})\b", RegexOptions.IgnoreCase)]
    private static partial Regex WorkOrderRegex();

    public static string Strip(string text) =>
        AnyMarkerRegex().Replace(text, string.Empty).TrimEnd();

    /// <summary>Extracts trusted chips from scripted text.</summary>
    public static IReadOnlyList<AskChip> Parse(string text)
    {
        var chips = new List<AskChip>();
        foreach (Match m in MarkerRegex().Matches(text))
        {
            var chip = Create(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value);
            if (chip is not null && !chips.Contains(chip))
            {
                chips.Add(chip);
            }
        }
        return chips;
    }

    /// <summary>
    /// Deterministic chips for live answers: keyword match over the question, answer and page, never
    /// from model-emitted markers.
    /// </summary>
    public static IReadOnlyList<AskChip> Derive(string question, string answer, string page)
    {
        var text = $"{question}\n{answer}";
        var chips = new List<AskChip>();

        void Add(AskChip? chip)
        {
            if (chip is not null && !chips.Contains(chip))
            {
                chips.Add(chip);
            }
        }

        // Canonical chips for the stage intents come first so the beat is identical in live and scripted modes.
        switch (AskContextBuilder.DetectIntent(question, page))
        {
            case AskIntent.WhyHeld:
                Add(Create("flight", FleetDataService.HailFlight.ToString()));
                Add(Create("part", "fanblade14"));
                Add(Create("nav", "inspections"));
                break;
            case AskIntent.SeenBefore:
                Add(Create("wo", FleetDataService.OpenHeroFanWorkOrder.ToString()));
                Add(Create("wo", FleetDataService.PriorHeroFanWorkOrder.ToString()));
                if (FleetDataService.Instance.RelatedWorkOrderNumber > 0)
                {
                    Add(Create("wo", FleetDataService.Instance.RelatedWorkOrderNumber.ToString()));
                }
                Add(Create("part", "fanblade14"));
                break;
            case AskIntent.Logbook:
                Add(Create("part", "fanblade14"));
                Add(Create("nav", "inspections"));
                break;
        }

        if (Has(text, "flight 44", "hail", "fl190", "den→sea", "den-sea", "step change") || (page == "Aircraft" && Has(text, "changed")))
        {
            Add(Create("flight", "44"));
        }
        if (Has(text, "fan blade", "blade 14", "blade", "fod", "nick", "blend", "fan"))
        {
            Add(Create("part", "fanblade14"));
        }
        // Only surface work orders that really exist; never trust a number the model produced.
        var known = FleetDataService.Instance.WorkOrders.Select(w => w.Number).ToHashSet();
        foreach (Match m in WorkOrderRegex().Matches(text))
        {
            if (int.TryParse(m.Groups[1].Value, out var n) && known.Contains(n))
            {
                Add(Create("wo", m.Groups[1].Value));
            }
        }
        if (Has(text, "ca 1916", "seen this before", "last year", "history"))
        {
            Add(Create("wo", FleetDataService.PriorHeroFanWorkOrder.ToString()));
            var related = FleetDataService.Instance.RelatedWorkOrderNumber;
            if (related > 0)
            {
                Add(Create("wo", related.ToString()));
            }
        }
        if (Has(text, "borescope", "72-00", "inspection"))
        {
            Add(Create("nav", "inspections"));
        }
        return chips.Take(4).ToList();
    }

    private static bool Has(string text, params string[] keys) =>
        keys.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static AskChip? Create(string kind, string value) => kind switch
    {
        "flight" when int.TryParse(value, out var n) => new AskChip(AskChipKind.Flight, n.ToString(), $"Flight {n}", "\uE709"),
        "part" when value.Equals("fanblade14", StringComparison.OrdinalIgnoreCase) => new AskChip(AskChipKind.Part, "fanblade14", "Fan blade 14", "\uE7FC"),
        "wo" when int.TryParse(value.Replace("WO-", string.Empty, StringComparison.OrdinalIgnoreCase), out var w) => new AskChip(AskChipKind.WorkOrder, w.ToString(), $"WO-{w}", "\uE90F"),
        "nav" when value.Equals("inspections", StringComparison.OrdinalIgnoreCase) => new AskChip(AskChipKind.Nav, "inspections", "Borescope · AMM 72-00", "\uE8B3"),
        "nav" when value.Equals("workorders", StringComparison.OrdinalIgnoreCase) => new AskChip(AskChipKind.Nav, "workorders", "Work orders", "\uE90F"),
        _ => null,
    };
}
