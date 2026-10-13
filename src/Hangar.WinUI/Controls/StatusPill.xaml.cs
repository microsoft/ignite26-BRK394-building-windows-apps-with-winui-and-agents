using Hangar.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Hangar.Controls;

/// <summary>Status pill whose colors come from theme resources via visual states.</summary>
public sealed partial class StatusPill : UserControl
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(WorkOrderStatus), typeof(StatusPill),
        new PropertyMetadata(WorkOrderStatus.Open, (d, _) => ((StatusPill)d).Apply(true)));

    public StatusPill()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply(false);
    }

    public WorkOrderStatus Status
    {
        get => (WorkOrderStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private void Apply(bool useTransitions)
    {
        var text = Status == WorkOrderStatus.InProgress ? "In progress" : Status.ToString();
        Label.Text = text;
        AutomationProperties.SetName(this, $"Status {text}");
        VisualStateManager.GoToState(this, Status.ToString(), useTransitions);
    }
}
