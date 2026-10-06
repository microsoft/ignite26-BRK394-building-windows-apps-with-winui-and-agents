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

    // Declared after all static seed tables so they are initialized first.
    public static FleetDataService Instance { get; } = new();

    public ObservableCollection<Aircraft> Fleet { get; } = [];

    public ObservableCollection<WorkOrder> WorkOrders { get; } = [];

    /// <summary>N1 vibration (IPS) for the last 50 flights of CA 2207 Engine 2.</summary>
    public IReadOnlyList<double> Vibration { get; private set; } = [];

    public IReadOnlyList<double> FleetAverageVibration { get; private set; } = [];

    public const double VibrationThreshold = 1.8;

    public IReadOnlyList<double> Availability30 { get; private set; } = [];

    public IReadOnlyList<double> AvailabilitySpark { get; private set; } = [];

    public IReadOnlyList<double> AogSpark { get; private set; } = [];

    public IReadOnlyList<double> OpenWoSpark { get; private set; } = [];

    public IReadOnlyList<double> OnTimeSpark { get; private set; } = [];

    public Aircraft Hero => Fleet.First(a => a.Tail == HeroTail);

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
        SignOffWorkOrder = null;
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
            if (i == 7)
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
            vib[i] = Math.Round(0.9 + rng.NextDouble() * 0.35 + Math.Sin(i / 5.0) * 0.08, 2);
        }

        vib[46] = 1.42;
        vib[47] = 1.61;
        vib[48] = 1.74;
        vib[49] = 2.31;
        Vibration = vib;
        FleetAverageVibration = avg;

        var availability = new double[30];
        for (int i = 0; i < 30; i++)
        {
            availability[i] = Math.Round(98.1 + Math.Sin(i / 3.2) * 0.5 + rng.NextDouble() * 0.5, 2);
        }

        availability[29] = 97.6;
        availability[28] = 98.3;
        Availability30 = availability;

        AvailabilitySpark = Spark(rng, 98.0, 0.6, 97.6);
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
