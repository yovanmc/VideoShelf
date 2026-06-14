// src/VideoShelf.App/Motion/ViewTransition.cs
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VideoShelf.App.Motion;

/// <summary>Plays a short fade+rise enter animation when a persistent view
/// (Visibility-toggled, never re-Loaded) becomes visible. Honors reduced motion.</summary>
public static class ViewTransition
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ViewTransition),
            new PropertyMetadata(false, OnEnabledChanged));
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    // Settable so MainWindow can wire it from the resolved IMotionPolicy at startup.
    public static System.Func<bool> ShouldAnimate { get; set; } = () => true;

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || !(bool)e.NewValue) return;
        fe.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is not true) return;
            if (!ShouldAnimate())
            {
                fe.Opacity = 1;
                if (fe.RenderTransform is TranslateTransform t0) t0.Y = 0;
                return;
            }
            fe.RenderTransformOrigin = new Point(0.5, 0);
            var tt = new TranslateTransform(0, 12);
            fe.RenderTransform = tt;
            fe.Opacity = 0;
            var dur = (Duration)fe.FindResource("AnimNormal");
            var ease = (IEasingFunction)fe.FindResource("EaseOut");
            fe.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(12, 0, dur) { EasingFunction = ease });
        };
    }
}
