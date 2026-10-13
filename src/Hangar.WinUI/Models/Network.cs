namespace Hangar.Models;

/// <summary>Station with geographic coordinates (degrees).</summary>
public sealed record Airport(string Code, double Lat, double Lon);

/// <summary>One historical leg flown by CA 2207 (1-based <see cref="Flight"/>).</summary>
public sealed record FlightLeg(int Flight, string From, string To, double Vibration, string? CrewReport = null);

/// <summary>An aircraft in the live 02:40 network picture: airborne along From→To, or parked at From.</summary>
public sealed record LiveFlight(string Tail, string From, string To, double P0, double DurationSeconds, bool IsAirborne, bool IsHero);
