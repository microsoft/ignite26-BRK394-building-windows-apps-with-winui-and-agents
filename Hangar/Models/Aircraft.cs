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
    public required string Tail { get; init; }

    public required string Flight { get; init; }

    public required string Type { get; init; }

    public required string Station { get; init; }

    public required string NextDeparture { get; init; }

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
