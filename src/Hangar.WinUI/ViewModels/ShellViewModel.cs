using CommunityToolkit.Mvvm.ComponentModel;
using Hangar.Models;
using Hangar.Services;
using Microsoft.UI.Dispatching;

namespace Hangar.ViewModels;

[WinRT.GeneratedBindableCustomProperty]
public sealed partial class SearchSuggestion(string glyph, string title, string detail, object target)
{
    public string Glyph { get; } = glyph;

    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public object Target { get; } = target;

    public override string ToString() => Title;
}

public sealed partial class ShellViewModel : ObservableObject
{
    private static readonly TimeSpan DemoClockOffset = TimeSpan.FromSeconds(48);

    private readonly FleetDataService _data = FleetDataService.Instance;
    private readonly DispatcherQueueTimer _timer;
    private DateTime _resetAt = DateTime.UtcNow;

    public ShellViewModel(DispatcherQueue dispatcher)
    {
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        _data.Reset += (_, _) => OnReset();
        _data.HeroStatusChanged += (_, _) => UpdateHeroState();
        UpdateHeroState();
        Tick();
    }

    [ObservableProperty]
    public partial string Countdown { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClockText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsReleased { get; set; }

    [ObservableProperty]
    public partial string ChipLabel { get; set; } = string.Empty;

    public System.Collections.ObjectModel.ObservableCollection<SearchSuggestion> Suggestions { get; } = [];

    public string FlightLabel => $"{FleetDataService.HeroFlight} · {FleetDataService.HeroTail}";

    public string DepartureLabel => $"DEP {FleetDataService.Departure:HH:mm}";

    public void UpdateSuggestions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Suggestions.Clear();
            return;
        }

        var q = text.Trim();
        var results = new List<SearchSuggestion>();
        results.AddRange(_data.Fleet
            .Where(a => a.Tail.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Flight.Replace(" ", string.Empty).Contains(q.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .Select(a => new SearchSuggestion("\uE709", a.Tail, $"{a.Flight} · {a.Type} · {a.StatusNote}", a)));
        results.AddRange(_data.WorkOrders
            .Where(w => w.NumberText.Contains(q, StringComparison.OrdinalIgnoreCase) || w.PartNumber.Contains(q, StringComparison.OrdinalIgnoreCase) || w.Component.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .Select(w => new SearchSuggestion("\uE9F9", $"{w.NumberText} · {w.Component}", $"{w.Tail} · {w.PartNumber} · {w.StatusText}", w)));
        Suggestions.Clear();
        foreach (var r in results)
        {
            Suggestions.Add(r);
        }
    }

    private void OnReset()
    {
        _resetAt = DateTime.UtcNow;
        UpdateHeroState();
        Tick();
    }

    private void UpdateHeroState()
    {
        IsReleased = _data.Hero.Status == AircraftStatus.Healthy;
        ChipLabel = IsReleased ? "Released" : "Eng 2 vib";
    }

    private void Tick()
    {
        var now = FleetDataService.Now + DemoClockOffset + (DateTime.UtcNow - _resetAt);
        var remaining = FleetDataService.Departure - now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        Countdown = $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m {remaining.Seconds:00}s";
        ClockText = now.ToString("HH:mm");
    }
}
