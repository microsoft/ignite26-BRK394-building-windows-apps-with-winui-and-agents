using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Hangar;

// Benchmark instrumentation, active only when HANGAR_BENCH_OUT is set.
public sealed partial class MainWindow
{
    private static readonly string? BenchOut =
        GetBenchArg("--bench-out=") ?? Environment.GetEnvironmentVariable("HANGAR_BENCH_OUT");
    private static readonly bool BenchAutoNav =
        Environment.GetCommandLineArgs().Contains("--autonav") || Environment.GetEnvironmentVariable("HANGAR_BENCH_AUTONAV") == "1";

    private static string? GetBenchArg(string prefix) =>
        Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim('"');

    private void InitBench()
    {
        if (string.IsNullOrEmpty(BenchOut))
        {
            return;
        }

        RootGrid.Loaded += async (_, _) =>
        {
            await NextFrameAsync();
            await NextFrameAsync();
            var ready = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var start = new DateTimeOffset(Process.GetCurrentProcess().StartTime).ToUnixTimeMilliseconds();
            File.WriteAllText(BenchOut, $"{{\"processStartEpochMs\":{start},\"readyEpochMs\":{ready},\"runtime\":\"winui\"}}");

            if (BenchAutoNav)
            {
                await RunAutoNavAsync();
            }
            else if (GetBenchArg("--page=") is { Length: > 0 } page)
            {
                NavView.SelectedItem = NavView.MenuItems.OfType<NavigationViewItem>()
                    .FirstOrDefault(i => string.Equals((string)i.Tag, page, StringComparison.OrdinalIgnoreCase)) ?? NavView.SelectedItem;
            }
        };
    }

    private async Task RunAutoNavAsync()
    {
        string[] order = ["Aircraft", "WorkOrders", "Inspections", "Fleet"];
        var navFile = BenchOut + ".nav.jsonl";
        await Task.Delay(1500);
        for (var round = 0; round < 3; round++)
        {
            foreach (var tag in order)
            {
                var item = NavView.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == tag);
                var sw = Stopwatch.StartNew();
                var loaded = new TaskCompletionSource();
                void OnNav(object s, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
                {
                    RootFrame.Navigated -= OnNav;
                    if (e.Content is FrameworkElement fe)
                    {
                        if (fe.IsLoaded)
                        {
                            loaded.TrySetResult();
                        }
                        else
                        {
                            fe.Loaded += (_, _) => loaded.TrySetResult();
                        }
                    }
                    else
                    {
                        loaded.TrySetResult();
                    }
                }

                RootFrame.Navigated += OnNav;
                NavView.SelectedItem = item;
                await loaded.Task;
                await NextFrameAsync();
                await NextFrameAsync();
                sw.Stop();
                File.AppendAllText(navFile, $"{{\"page\":\"{tag}\",\"round\":{round},\"ms\":{sw.Elapsed.TotalMilliseconds:F2}}}\n");
                await Task.Delay(700);
            }
        }

        File.AppendAllText(navFile, "{\"done\":true}\n");
    }

    private static Task NextFrameAsync()
    {
        var tcs = new TaskCompletionSource();
        void Handler(object? s, object e)
        {
            CompositionTarget.Rendering -= Handler;
            tcs.TrySetResult();
        }

        CompositionTarget.Rendering += Handler;
        return tcs.Task;
    }
}
