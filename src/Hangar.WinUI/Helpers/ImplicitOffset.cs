using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Hangar.Helpers;

public static class ImplicitOffset
{
    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(ImplicitOffset), new PropertyMetadata(false, OnIsEnabledChanged));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            if (element is FrameworkElement { IsLoaded: false } frameworkElement)
            {
                frameworkElement.Loaded -= OnElementLoaded;
                frameworkElement.Loaded += OnElementLoaded;
                return;
            }

            Apply(element);
        }
        else
        {
            if (element is FrameworkElement frameworkElement)
            {
                frameworkElement.Loaded -= OnElementLoaded;
            }

            ElementCompositionPreview.GetElementVisual(element).ImplicitAnimations = null;
        }
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= OnElementLoaded;
        Apply(element);
    }

    private static void Apply(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1.0f));
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Target = "Offset";
        animation.Duration = TimeSpan.FromMilliseconds(400);
        animation.InsertExpressionKeyFrame(1.0f, "this.FinalValue", easing);

        var animations = compositor.CreateImplicitAnimationCollection();
        animations["Offset"] = animation;
        visual.ImplicitAnimations = animations;
    }
}
