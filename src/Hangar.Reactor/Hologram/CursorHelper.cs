using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

namespace Hangar.Reactor.Hologram;

/// <summary>
/// Reactor has no cursor modifier and <see cref="UIElement.ProtectedCursor"/> is protected, so the stage sets it
/// through reflection (the XAML app did the same from a UserControl subclass).
/// </summary>
internal static class CursorHelper
{
    private static readonly PropertyInfo? Property =
        typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static InputSystemCursorShape? _last;
    private static InputCursor? _cursor;

    public static void Set(UIElement element, InputSystemCursorShape shape)
    {
        try
        {
            if (_last != shape || _cursor is null)
            {
                _cursor = InputSystemCursor.Create(shape);
                _last = shape;
            }

            Property?.SetValue(element, _cursor);
        }
        catch
        {
            // Cursor feedback is cosmetic.
        }
    }
}
