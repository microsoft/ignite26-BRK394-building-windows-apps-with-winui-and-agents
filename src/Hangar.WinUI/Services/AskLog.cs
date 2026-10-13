namespace Hangar.Services;

internal static class AskLog
{
    public static void Write(string message) => System.Diagnostics.Debug.WriteLine($"[AskHangar] {message}");

    public static string Short(Exception ex) => $"0x{ex.HResult:X8}";
}
