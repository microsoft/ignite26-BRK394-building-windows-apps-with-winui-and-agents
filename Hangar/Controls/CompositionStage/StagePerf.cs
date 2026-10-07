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

    public static void Report(string label, Visual root, double buildMs)
    {
        var line = $"{DateTime.Now:O} [{label}] visuals={CountVisuals(root)} build={buildMs:F1}ms";
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
