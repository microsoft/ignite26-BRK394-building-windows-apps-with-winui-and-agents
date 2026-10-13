using Hangar.Controls;
using Hangar.Models;
using Hangar.Services;
using Hangar.ViewModels;
using Hangar.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Hangar;

/// <summary>Search payload that opens the Ask Hangar pane with a question.</summary>
public sealed record AskQuery(string Text);

public sealed partial class MainWindow
{
    private void InitAsk()
    {
        AskPane.ChipInvoked += OnAskChipInvoked;
        AskPane.UseInSignOffRequested += OnAskUseInSignOff;
        LocalModelService.Instance.ModeChanged += (_, _) => DispatcherQueue.TryEnqueue(AskPane.RefreshMode);
        _ = LocalModelService.Instance.InitializeAsync();
    }

    private void AddAskSuggestion(string text)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t))
        {
            SearchBox.ItemsSource = ViewModel.Suggestions;
            return;
        }

        // Assign a fresh list so this works whatever collection shape the view model exposes.
        SearchBox.ItemsSource = new List<SearchSuggestion>(ViewModel.Suggestions)
        {
            new("\uE945", $"Ask Hangar: {t}", LocalModelService.Instance.ActiveModelName is { } model ? $"On-device answer · {model}" : "On-device assistant", new AskQuery(t)),
        };
    }

    private void OnAskToggleInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        AskPane.Toggle();
    }

    private void OnAskForceScriptedInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        LocalModelService.Instance.ForceScripted = !LocalModelService.Instance.ForceScripted;
        AskPane.RefreshMode();
        AskPane.Open();
    }

    private void OnAskButtonClick(object sender, RoutedEventArgs e) => AskPane.Toggle();

    private void OnAskChipInvoked(object? sender, AskChip chip)
    {
        switch (chip.Kind)
        {
            case AskChipKind.Flight:
                var flight = int.TryParse(chip.Value, out var f) ? f : 44;
                OnPage<AircraftPage>(
                    () => NavigationService.Navigate(typeof(AircraftPage), new AircraftNavigationArgs(FleetDataService.HeroTail)),
                    p =>
                    {
                        p.ScrubToFlight(flight);
                        p.ExpandGlobe();
                    });
                break;

            case AskChipKind.Part:
                NavigationService.Navigate(typeof(AircraftPage), new AircraftNavigationArgs(FleetDataService.HeroTail, EnginePart.Fan));
                break;

            case AskChipKind.WorkOrder:
                NavigationService.Navigate(typeof(WorkOrdersPage), new WorkOrdersNavigationArgs(chip.Label));
                break;

            case AskChipKind.Nav when chip.Value == "inspections":
                NavigationService.Navigate(typeof(InspectionsPage), new InspectionNavigationArgs(FleetDataService.HeroTail, "Fan blade 14 · leading-edge nick"));
                break;

            default:
                NavigationService.Navigate(typeof(WorkOrdersPage), new WorkOrdersNavigationArgs(null));
                break;
        }
    }

    private void OnAskUseInSignOff(object? sender, string entry)
    {
        OnPage<InspectionsPage>(
            () => NavigationService.Navigate(typeof(InspectionsPage), new InspectionNavigationArgs(FleetDataService.HeroTail, "Fan blade 14 · leading-edge nick")),
            p => p.PrefillReleaseEntry(entry));
    }

    /// <summary>Runs <paramref name="act"/> on the page now, or after navigating and the new page has loaded.</summary>
    private void OnPage<TPage>(Action navigate, Action<TPage> act)
        where TPage : FrameworkElement
    {
        if (RootFrame.Content is TPage current)
        {
            act(current);
            return;
        }

        navigate();
        if (RootFrame.Content is TPage page)
        {
            void Loaded(object s, RoutedEventArgs e)
            {
                page.Loaded -= Loaded;
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => act(page));
            }

            page.Loaded += Loaded;
        }
    }
}
