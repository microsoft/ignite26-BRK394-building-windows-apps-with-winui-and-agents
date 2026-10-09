using System.Collections.ObjectModel;
using Hangar.Models;

namespace Hangar.Services;

/// <summary>
/// Deterministic, in-memory "02:40" dataset (seed 2207). No network, no disk.
/// </summary>
public sealed class FleetDataService
{
    public const string HeroTail = "N2207C";
    public const string HeroFlight = "CA 2207";
    public const int Seed = 2207;


    public static readonly string[] Components =
    [
        "Fan blade", "HPT blade", "Compressor", "Nacelle", "Landing gear", "Hydraulics",
        "Avionics", "Brakes", "APU", "Cabin", "Fuel system", "Flight controls",
    ];

    private static readonly Dictionary<string, string> AtaByComponent = new()
    {
        ["Fan blade"] = "72-21", ["HPT blade"] = "72-51", ["Compressor"] = "72-31", ["Nacelle"] = "71-11",
        ["Landing gear"] = "32-11", ["Hydraulics"] = "29-11", ["Avionics"] = "34-41", ["Brakes"] = "32-42",
        ["APU"] = "49-10", ["Cabin"] = "25-21", ["Fuel system"] = "28-21", ["Flight controls"] = "27-51",
    };

    private static readonly Dictionary<string, string[]> FindingsByComponent = new()
    {
        ["Fan blade"] = ["Leading-edge nick, within limits", "Erosion on blade tip", "FOD strike, blend repair", "Vibration trend review"],
        ["HPT blade"] = ["Coating loss stage 1", "Tip curl, monitor", "Thermal distress noted"],
        ["Compressor"] = ["Wash performed", "Stage 3 blade dent", "Bleed valve sticking"],
        ["Nacelle"] = ["Fan cowl latch worn", "Thrust reverser seal", "Acoustic panel delam"],
        ["Landing gear"] = ["Shock strut servicing", "Tire wear at limit", "Downlock actuator leak"],
        ["Hydraulics"] = ["Green system pressure low", "Line chafing", "Reservoir overfill"],
        ["Avionics"] = ["ADIRU fault msg", "Weather radar intermittent", "FMS database load"],
        ["Brakes"] = ["Wear pin at limit", "Brake temp high #3", "Accumulator precharge"],
        ["APU"] = ["Oil consumption high", "Start fault", "Inlet door actuator"],
        ["Cabin"] = ["Seat 14C recline", "Lav smoke detector", "PSU light inop"],
        ["Fuel system"] = ["Quantity indication drift", "Boost pump pressure low", "Vent valve"],
        ["Flight controls"] = ["Spoiler 3 servo", "Flap asymmetry msg", "Trim actuator play"],
    };

    public static readonly string[] Technicians =
    [
        "Maya Okafor", "Diego Ramos", "Priya Natarajan", "Tom Becker", "Aiko Tanaka", "Samuel Mensah",
        "Elena Petrova", "Jonas Lindqvist", "Fatima Haddad", "Chris Walker", "Lucía Fernández", "Wei Chen",
    ];

    private static readonly string[] Types = ["A321neo", "A320neo", "A321neo", "737 MAX 8", "A220-300"];
    private static readonly string[] Stations = ["SEA", "SFO", "DEN", "ORD", "JFK", "LAX", "ATL", "BOS"];

    public static readonly DateTime Now = new(2026, 11, 18, 2, 40, 0);
    public static readonly DateTime Departure = new(2026, 11, 18, 6, 15, 0);

    /// <summary>Network airports (real lat/lon, degrees).</summary>
    public static readonly IReadOnlyDictionary<string, Airport> Airports = new Dictionary<string, Airport>
    {
        ["SEA"] = new("SEA", 47.449, -122.309),
        ["PDX"] = new("PDX", 45.589, -122.597),
        ["YVR"] = new("YVR", 49.195, -123.184),
        ["ANC"] = new("ANC", 61.174, -149.996),
        ["HNL"] = new("HNL", 21.319, -157.922),
        ["SFO"] = new("SFO", 37.619, -122.375),
        ["LAX"] = new("LAX", 33.942, -118.408),
        ["LAS"] = new("LAS", 36.084, -115.153),
        ["PHX"] = new("PHX", 33.437, -112.008),
        ["DEN"] = new("DEN", 39.856, -104.674),
        ["DFW"] = new("DFW", 32.897, -97.038),
        ["MSP"] = new("MSP", 44.885, -93.222),
        ["ORD"] = new("ORD", 41.979, -87.905),
        ["ATL"] = new("ATL", 33.641, -84.427),
        ["MIA"] = new("MIA", 25.796, -80.287),
        ["IAD"] = new("IAD", 38.953, -77.456),
        ["JFK"] = new("JFK", 40.641, -73.778),
        ["BOS"] = new("BOS", 42.366, -71.010),
    };

    // CA 2207 itinerary: stop[i] -> stop[i + 1] is flight i + 1. Flight 44 is DEN -> SEA (hail).
    private static readonly string[] HeroStops =
    [
        "SEA", "LAX", "SEA", "ANC", "SEA", "JFK", "SEA", "SFO", "HNL", "SFO",
        "SEA", "ORD", "SEA", "DEN", "PHX", "SEA", "BOS", "SEA", "LAS", "SEA",
        "MSP", "SEA", "ATL", "SEA", "IAD", "SEA", "DFW", "SEA", "MIA", "SEA",
        "HNL", "SEA", "LAX", "DEN", "SEA", "JFK", "SEA", "SFO", "SEA", "ORD",
        "SEA", "PHX", "SEA", "DEN", "SEA", "BOS", "SEA", "LAX", "SEA", "ANC",
        "SEA",
    ];

    public const int HailFlight = 44;

    /// <summary>Zero-based scrubber index of <see cref="HailFlight"/>.</summary>
    public const int HailFlightIndex = HailFlight - 1;
    public const string HailReport = "Hail encounter on climb, FL190";

    // Red-eyes in the air at 02:40 PT: (from, to, progress at 02:40, demo seconds to arrival from 0).
    private static readonly (string From, string To, double P0, double Duration)[] RedEyes =
    [
        ("SEA", "JFK", 0.42, 7200), ("LAX", "BOS", 0.55, 6600), ("SFO", "ATL", 0.48, 7000),
        ("HNL", "SFO", 0.63, 6000), ("ANC", "SEA", 0.71, 5400), ("LAS", "MIA", 0.37, 7800),
        ("PHX", "IAD", 0.52, 6900), ("SFO", "ORD", 0.66, 5800), ("DEN", "BOS", 0.29, 8000),
        ("PDX", "DFW", 0.58, 6300), ("LAX", "JFK", 0.33, 8200), ("SEA", "MIA", 0.24, 8600),
    ];

    // Declared after all static seed tables so they are initialized first.
    public static FleetDataService Instance { get; } = new();

    /// <summary>CA 2207's last 50 legs (flight 1..50), chained, ending at SEA.</summary>
    public IReadOnlyList<FlightLeg> Legs { get; private set; } = [];

    /// <summary>Every aircraft's position at 02:40 PT: ~12 airborne red-eyes, the rest parked.</summary>
    public IReadOnlyList<LiveFlight> LiveFlights { get; private set; } = [];

    public int AirborneCount => LiveFlights.Count(f => f.IsAirborne);

    private readonly System.Diagnostics.Stopwatch _networkClock = new();

    /// <summary>Route progress 0..1 for an airborne flight; advances slowly in real time from demo reset.</summary>
    public double Progress(LiveFlight flight) =>
        flight.IsAirborne
            ? Math.Clamp(flight.P0 + _networkClock.Elapsed.TotalSeconds / flight.DurationSeconds, 0, 1)
            : 0;

    public ObservableCollection<Aircraft> Fleet { get; } = [];

    public ObservableCollection<WorkOrder> WorkOrders { get; } = [];

    /// <summary>N1 vibration (IPS) for the last 50 flights of CA 2207 Engine 2.</summary>
    public IReadOnlyList<double> Vibration { get; private set; } = [];

    public IReadOnlyList<double> FleetAverageVibration { get; private set; } = [];

    public const double VibrationThreshold = 1.8;

    public const double AvailabilityTarget = 98.0;

    /// <summary>
    /// Single source of truth for today's fleet availability (%). Drives the KPI value, the sparkline
    /// endpoint, the hero chart endpoint/marker and the "Today" badge. 97.6% while CA 2207 is held.
    /// </summary>
    public double TodayAvailability => Hero.Status == AircraftStatus.Healthy ? 98.1 : 97.6;

    private double[] _availabilityHistory = [];
    private double[] _availabilitySparkHistory = [];

    /// <summary>30-day availability; the last point is always <see cref="TodayAvailability"/>.</summary>
    public IReadOnlyList<double> Availability30 => WithToday(_availabilityHistory);

    /// <summary>KPI sparkline; the last point is always <see cref="TodayAvailability"/>.</summary>
    public IReadOnlyList<double> AvailabilitySpark => WithToday(_availabilitySparkHistory);

    private double[] WithToday(double[] history)
    {
        var values = (double[])history.Clone();
        if (values.Length > 0)
        {
            values[^1] = TodayAvailability;
        }

        return values;
    }

    public IReadOnlyList<double> AogSpark { get; private set; } = [];

    public IReadOnlyList<double> OpenWoSpark { get; private set; } = [];

    public IReadOnlyList<double> OnTimeSpark { get; private set; } = [];

    public Aircraft Hero => Fleet.First(a => a.Tail == HeroTail);

    public const string RelatedFlight = "CA 1916";

    private const int RelatedFleetIndex = 11;

    private const int RelatedWorkOrderIndex = 57;

    public const int PriorHeroFanWorkOrder = 46_871;

    public const int OpenHeroFanWorkOrder = 50_498;

    /// <summary>Tail of CA 1916, the sister A321neo with last year's matching fan-blade fault.</summary>
    public string RelatedTail => Fleet[RelatedFleetIndex].Tail;

    /// <summary>Work order number of CA 1916's fan-blade blend repair (last year).</summary>
    public int RelatedWorkOrderNumber { get; private set; }

    public WorkOrder? SignOffWorkOrder { get; private set; }

    public event EventHandler? Reset;

    public event EventHandler? HeroStatusChanged;

    private FleetDataService() => Seed2240();

    public void ResetDemo()
    {
        Seed2240();
        Reset?.Invoke(this, EventArgs.Empty);
    }

    public int OpenWorkOrderCount => 128 + (Hero.Status == AircraftStatus.Healthy ? -1 : 0);

    public void SignOff(string technician, string note)
    {
        var wo = WorkOrders.FirstOrDefault(w => w.Tail == HeroTail && w.Status != WorkOrderStatus.Closed && w.Component == "Fan blade");
        if (wo is null)
        {
            wo = new WorkOrder
            {
                Number = WorkOrders.Max(w => w.Number) + 1,
                Tail = HeroTail,
                Component = "Fan blade",
                Ata = AtaByComponent["Fan blade"],
                PartNumber = "2307M41P04",
                Technician = technician,
                Opened = Now,
                MappedPart = EnginePart.Fan,
                Hours = 1.5,
            };
            WorkOrders.Insert(0, wo);
        }

        wo.Finding = note;
        wo.Status = WorkOrderStatus.Closed;
        wo.SignedBy = technician;
        SignOffWorkOrder = wo;

        Hero.Status = AircraftStatus.Healthy;
        Hero.StatusNote = "Released to service · 03:12";
        HeroStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Seed2240()
    {
        var rng = new Random(Seed);
        SeedFleet(rng);
        SeedWorkOrders(rng);
        SeedTelemetry(rng);
        SeedNetwork();
        SignOffWorkOrder = null;
    }

    private void SeedNetwork()
    {
        var legs = new FlightLeg[50];
        for (int i = 0; i < 50; i++)
        {
            legs[i] = new FlightLeg(i + 1, HeroStops[i], HeroStops[i + 1], Vibration[i],
                i + 1 == HailFlight ? HailReport : null);
        }

        Legs = legs;

        var live = new List<LiveFlight>(Fleet.Count);
        int airborne = 0;
        foreach (var aircraft in Fleet)
        {
            bool hero = aircraft.Tail == HeroTail;
            if (!hero && aircraft.Status == AircraftStatus.Healthy && airborne < RedEyes.Length)
            {
                var r = RedEyes[airborne++];
                live.Add(new LiveFlight(aircraft.Tail, r.From, r.To, r.P0, r.Duration, true, false));
            }
            else
            {
                var station = hero ? "SEA" : aircraft.Station;
                live.Add(new LiveFlight(aircraft.Tail, station, station, 0, 1, false, hero));
            }
        }

        LiveFlights = live;
        _networkClock.Restart();
    }

    private void SeedFleet(Random rng)
    {
        Fleet.Clear();
        var used = new HashSet<string> { HeroTail };
        for (int i = 0; i < 42; i++)
        {
            string tail;
            string flight;
            string type;
            if (i == 2)
            {
                tail = HeroTail;
                flight = HeroFlight;
                type = "A321neo";
            }
            else
            {
                do
                {
                    tail = $"N{rng.Next(100, 999)}{(char)('A' + rng.Next(0, 26))}{(char)('A' + rng.Next(0, 26))}";
                }
                while (!used.Add(tail));
                flight = $"CA {rng.Next(100, 2999)}";
                type = Types[rng.Next(Types.Length)];
            }

            var status = AircraftStatus.Healthy;
            var note = "Ready";
            if (tail == HeroTail)
            {
                status = AircraftStatus.Attention;
                note = "Eng 2 vibration exceedance";
            }
            else if (i == 23)
            {
                status = AircraftStatus.Aog;
                note = "AOG · Hydraulic pump";
            }
            else if (i is 3 or 15 or 31 or 38)
            {
                status = AircraftStatus.Maintenance;
                note = "Scheduled A-check";
            }

            if (i == RelatedFleetIndex)
            {
                // Sister A321neo that had the same fan-blade fault last year (Ask Hangar history).
                flight = RelatedFlight;
                type = "A321neo";
            }

            Fleet.Add(new Aircraft
            {
                Tail = tail,
                Flight = flight,
                Type = type,
                Station = tail == HeroTail ? "SEA" : Stations[rng.Next(Stations.Length)],
                NextDeparture = tail == HeroTail ? "06:15" : $"{rng.Next(5, 12):00}:{rng.Next(0, 12) * 5:00}",
                Cycles = tail == HeroTail ? 8_412 : rng.Next(1_200, 14_000),
                FlightHours = tail == HeroTail ? 19_877 : rng.Next(3_000, 32_000),
                DaysToNextCheck = tail == HeroTail ? 23 : rng.Next(2, 90),
                Status = status,
                StatusNote = note,
            });
        }
    }

    private void SeedWorkOrders(Random rng)
    {
        WorkOrders.Clear();
        var list = new List<WorkOrder>(2400);
        int number = 48_100;
        for (int i = 0; i < 2_398; i++)
        {
            var component = Components[rng.Next(Components.Length)];
            var findings = FindingsByComponent[component];
            var aircraft = Fleet[rng.Next(Fleet.Count)];
            var opened = Now.AddDays(-rng.Next(0, 730)).AddMinutes(-rng.Next(0, 1440));
            var ageDays = (Now - opened).TotalDays;
            var status = ageDays > 21
                ? (rng.NextDouble() < 0.94 ? WorkOrderStatus.Closed : WorkOrderStatus.Deferred)
                : (WorkOrderStatus)rng.Next(0, 4);

            list.Add(new WorkOrder
            {
                Number = number++,
                Tail = aircraft.Tail,
                Component = component,
                Ata = AtaByComponent[component],
                PartNumber = PartNumber(rng),
                Technician = Technicians[rng.Next(Technicians.Length)],
                Opened = opened,
                MappedPart = MapPart(component),
                Finding = findings[rng.Next(findings.Length)],
                Status = status,
                Hours = Math.Round(0.5 + rng.NextDouble() * 11.5, 1),
            });
        }

        // The "we've seen this before" moment: matching fan-blade fault 11 months ago.
        list.Add(new WorkOrder
        {
            Number = 46_871,
            Tail = HeroTail,
            Component = "Fan blade",
            Ata = AtaByComponent["Fan blade"],
            PartNumber = "2307M41P04",
            Technician = "Diego Ramos",
            Opened = Now.AddMonths(-11).AddDays(-3),
            MappedPart = EnginePart.Fan,
            Finding = "Eng 2 N1 vibration · blade 14 nick, blended",
            Status = WorkOrderStatus.Closed,
            Hours = 6.5,
        });

        // Same fault on CA 1916 last year: overwrite one generated record so the RNG sequence and the
        // 2,400 total stay unchanged.
        RelatedWorkOrderNumber = list[RelatedWorkOrderIndex].Number;
        list[RelatedWorkOrderIndex] = new WorkOrder
        {
            Number = RelatedWorkOrderNumber,
            Tail = RelatedTail,
            Component = "Fan blade",
            Ata = AtaByComponent["Fan blade"],
            PartNumber = "2307M41P04",
            Technician = "Maya Okafor",
            Opened = Now.AddMonths(-12).AddDays(-9).AddHours(-3),
            MappedPart = EnginePart.Fan,
            Finding = "Eng 2 fan blade 14 LE nick · blend repair per AMM 72-21 · returned to service",
            Status = WorkOrderStatus.Closed,
            Hours = 5.5,
        };

        // Tonight's open work order.
        list.Add(new WorkOrder
        {
            Number = 50_498,
            Tail = HeroTail,
            Component = "Fan blade",
            Ata = AtaByComponent["Fan blade"],
            PartNumber = "2307M41P04",
            Technician = "Maya Okafor",
            Opened = Now.AddMinutes(-26),
            MappedPart = EnginePart.Fan,
            Finding = "Eng 2 N1 vibration exceedance · borescope",
            Status = WorkOrderStatus.InProgress,
            Hours = 1.0,
        });

        foreach (var wo in list.OrderByDescending(w => w.Opened))
        {
            WorkOrders.Add(wo);
        }
    }

    private void SeedTelemetry(Random rng)
    {
        var vib = new double[50];
        var avg = new double[50];
        for (int i = 0; i < 50; i++)
        {
            avg[i] = 0.95 + Math.Sin(i / 7.0) * 0.05;
            vib[i] = Math.Round(0.86 + rng.NextDouble() * 0.2 + Math.Sin(i / 5.0) * 0.05, 2);
        }

        // Flight 44 (DEN -> SEA, hail on climb) is the step change; it trends up to 2.31 at flight 50.
        vib[43] = 1.32;
        vib[44] = 1.38;
        vib[45] = 1.47;
        vib[46] = 1.58;
        vib[47] = 1.69;
        vib[48] = 1.79;
        vib[49] = 2.31;
        Vibration = vib;
        FleetAverageVibration = avg;

        var availability = new double[30];
        for (int i = 0; i < 30; i++)
        {
            availability[i] = Math.Round(98.1 + Math.Sin(i / 3.2) * 0.5 + rng.NextDouble() * 0.5, 2);
        }

        availability[28] = 98.3;
        _availabilityHistory = availability;

        _availabilitySparkHistory = Spark(rng, AvailabilityTarget, 0.6, AvailabilityTarget);
        AogSpark = [2, 1, 1, 0, 0, 1, 2, 1, 0, 0, 1, 1, 0, 0, 1, 1];
        OpenWoSpark = Spark(rng, 135, 8, 128);
        OnTimeSpark = Spark(rng, 92, 2.5, 94);
    }

    private static double[] Spark(Random rng, double center, double amplitude, double last)
    {
        var values = new double[16];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Math.Round(center + (rng.NextDouble() - 0.5) * 2 * amplitude, 2);
        }

        values[^1] = last;
        return values;
    }

    private static string PartNumber(Random rng) =>
        $"{rng.Next(1000, 9999)}M{rng.Next(10, 99)}P{rng.Next(1, 20):00}";

    private static EnginePart MapPart(string component) => component switch
    {
        "Fan blade" => EnginePart.Fan,
        "Compressor" => EnginePart.Compressor,
        "HPT blade" => EnginePart.Turbine,
        "Nacelle" => EnginePart.Nacelle,
        _ => EnginePart.None,
    };
}
