using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hangar.Models;

public enum WorkOrderStatus
{
    Open,
    InProgress,
    Deferred,
    Closed,
}

public enum EnginePart
{
    None,
    Nacelle,
    Fan,
    Compressor,
    Turbine,
}

public sealed partial class WorkOrder : ObservableValidator
{
    public required int Number { get; init; }

    public string NumberText => $"WO-{Number}";

    public required string Tail { get; init; }

    public required string Component { get; init; }

    public required string Ata { get; init; }

    public required string PartNumber { get; init; }

    public required string Technician { get; init; }

    public required DateTime Opened { get; init; }

    public string OpenedText => Opened.ToString("dd MMM yyyy");

    public EnginePart MappedPart { get; init; }

    public bool IsMappable => MappedPart != EnginePart.None;

    [ObservableProperty]
    public partial string Finding { get; set; } = string.Empty;

    [ObservableProperty]
    public partial WorkOrderStatus Status { get; set; }

    [ObservableProperty]
    public partial string? SignedBy { get; set; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0.1, 500, ErrorMessage = "Hours must be greater than 0")]
    public partial double Hours { get; set; }

    public string StatusText => Status switch
    {
        WorkOrderStatus.InProgress => "In progress",
        _ => Status.ToString(),
    };

    partial void OnStatusChanged(WorkOrderStatus value) => OnPropertyChanged(nameof(StatusText));
}
