#if DEBUG
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Hangar.Reactor;

// Debug-only: renders the window's XAML tree to a PNG (Ctrl+Shift+P) for docs screenshots.
static class DebugCapture
{
    const string OutputDir = @"D:\Hangar\docs\screenshots\reactor";

    public static async Task SaveAsync(UIElement root, string fileName, bool dark)
    {
        try
        {
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(root);
            var pixels = (await rtb.GetPixelsAsync()).ToArray();

            // RenderTargetBitmap can't capture the Mica backdrop; composite over its approximate base color.
            byte bg = dark ? (byte)0x20 : (byte)0xF3;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int inv = 255 - pixels[i + 3];
                for (int c = 0; c < 3; c++)
                    pixels[i + c] = (byte)Math.Min(255, pixels[i + c] + bg * inv / 255);
                pixels[i + 3] = 255;
            }
            var scale = root.XamlRoot?.RasterizationScale ?? 1.0;

            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96 * scale, 96 * scale, pixels);
            await encoder.FlushAsync();

            stream.Seek(0);
            var bytes = new byte[stream.Size];
            await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);

            try
            {
                Directory.CreateDirectory(OutputDir);
                await File.WriteAllBytesAsync(Path.Combine(OutputDir, fileName), bytes);
            }
            catch
            {
                await File.WriteAllBytesAsync(Path.Combine(ApplicationData.Current.LocalFolder.Path, fileName), bytes);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Capture failed: {ex}");
        }
    }
}
#endif
