using CommunityToolkit.Mvvm.ComponentModel;
using Hangar.Services;

namespace Hangar.Models;

public enum AircraftStatus
{
    Healthy,
    Attention,
    Maintenance,
    Aog,
}

public sealed partial class Aircraft : ObservableObject
{
    public string Tail { get; init; } = string.Empty;

    public string Flight { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Station { get; init; } = string.Empty;

    public string NextDeparture { get; init; } = string.Empty;

    public int Cycles { get; init; }

    public int FlightHours { get; init; }

    public int DaysToNextCheck { get; init; }

    public string NextCheck { get; init; } = "A-check";

    [ObservableProperty]
    public partial AircraftStatus Status { get; set; }

    [ObservableProperty]
    public partial string StatusNote { get; set; } = string.Empty;

    public bool IsHero => Tail == FleetDataService.HeroTail;
}
