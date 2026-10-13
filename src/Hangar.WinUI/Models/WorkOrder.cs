using System.Collections;
using System.ComponentModel;
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

// Hand-rolled INotifyDataErrorInfo instead of ObservableValidator: DataAnnotations validation
// relies on reflection that Native AOT trimming can't preserve.
[WinRT.GeneratedBindableCustomProperty]
public sealed partial class WorkOrder : ObservableObject, INotifyDataErrorInfo
{
    private const double MinHours = 0.1;
    private const double MaxHours = 500;
    private const string HoursError = "Hours must be greater than 0";

    private bool _hoursInvalid;

    public int Number { get; init; }

    public string NumberText => $"WO-{Number}";

    public string Tail { get; init; } = string.Empty;

    public string Component { get; init; } = string.Empty;

    public string Ata { get; init; } = string.Empty;

    public string PartNumber { get; init; } = string.Empty;

    public string Technician { get; init; } = string.Empty;

    public DateTime Opened { get; init; }

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
    public partial string? SignaturePath { get; set; }

    [ObservableProperty]
    public partial double Hours { get; set; }

    public bool HasErrors => _hoursInvalid;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName) =>
        _hoursInvalid && (string.IsNullOrEmpty(propertyName) || propertyName == nameof(Hours))
            ? new[] { HoursError }
            : Array.Empty<string>();

    partial void OnHoursChanged(double value)
    {
        var invalid = double.IsNaN(value) || value < MinHours || value > MaxHours;
        if (invalid == _hoursInvalid)
        {
            return;
        }

        _hoursInvalid = invalid;
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Hours)));
        OnPropertyChanged(nameof(HasErrors));
    }

    public string StatusText => Status switch
    {
        WorkOrderStatus.InProgress => "In progress",
        _ => Status.ToString(),
    };

    partial void OnStatusChanged(WorkOrderStatus value) => OnPropertyChanged(nameof(StatusText));
}
