using Hangar.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Hangar.Services;

public sealed record AircraftNavigationArgs(string Tail, EnginePart Part = EnginePart.None, bool FromFleetTile = false);

public sealed record WorkOrdersNavigationArgs(string? Query);

public static class NavigationService
{
    private static Frame? _frame;

    public static event EventHandler<Type>? Navigated;

    public static void Initialize(Frame frame)
    {
        _frame = frame;
        _frame.Navigated += (_, e) => Navigated?.Invoke(null, e.SourcePageType);
    }

    public static Type? CurrentPage => _frame?.CurrentSourcePageType;

    public static void Navigate(Type page, object? parameter = null, NavigationTransitionInfo? transition = null)
    {
        if (_frame is null)
        {
            return;
        }

        if (_frame.CurrentSourcePageType == page && parameter is null)
        {
            return;
        }

        _frame.Navigate(page, parameter, transition ?? new EntranceNavigationTransitionInfo());
    }
}
