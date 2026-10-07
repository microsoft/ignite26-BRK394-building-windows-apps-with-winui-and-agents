using Hangar.Models;
using Microsoft.UI.Xaml;

namespace Hangar.Controls;

public interface IAircraftStage
{
    event EventHandler<EnginePart>? PartSelected;
    event EventHandler<bool>? ExplodedChanged;

    bool IsExploded { get; }
    EnginePart SelectedPart { get; }
    Thickness FocusPadding { get; set; }

    void SetExploded(bool exploded);
    void ExplodeEngine();
    void CollapseEngine();
    void Select(EnginePart part);
    void SetHeat(double value);
    void SetStatus(AircraftStatus status);
    void ResetView();
    void Celebrate();
}
