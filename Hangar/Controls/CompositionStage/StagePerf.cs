using System.Diagnostics;
using Microsoft.UI.Composition;
using Windows.Storage;

namespace Hangar.Controls.CompositionStage;

internal static class StagePerf
{
    public static int CountVisuals(Visual root)
    {
        var count = 1;
        if (root is ContainerVisual container)
        {
            foreach (var child in container.Children)
            {
                count += CountVisuals(child);
            }
        }

        return count;
    }

    public static void Report(string label, Visual root, double buildMs, double bakeMs = 0, int cached = 0, int baked = 0)
    {
        var suffix = bakeMs > 0 || cached > 0 || baked > 0 ? $" bake={bakeMs:F1}ms cached={cached} baked={baked}" : string.Empty;
        var line = $"{DateTime.Now:O} [{label}] visuals={CountVisuals(root)} build={buildMs:F1}ms{suffix}";
        Debug.WriteLine($"[CompositionStage] {line}");
        try
        {
            File.AppendAllText(Path.Combine(ApplicationData.Current.LocalFolder.Path, "stage-perf.log"), line + Environment.NewLine);
        }
        catch
        {
        }
    }
}
