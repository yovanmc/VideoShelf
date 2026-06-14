using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VideoShelf.App.Views.Controls;

/// <summary>
/// Skeleton placeholder panel shown while a list view is loading.
/// Displays rounded placeholder Borders (reduced-motion baseline).
/// When <see cref="Animate"/> is true, a shimmer sweep Storyboard plays.
/// When false (OS reduced-motion / IMotionPolicy.ShouldAnimate = false), the
/// static placeholder rectangles are shown with no animation — no fake motion.
/// </summary>
public partial class SkeletonPanel : UserControl
{
    private Storyboard? _shimmerBoard;

    // ── DependencyProperty ────────────────────────────────────────────────────

    /// <summary>
    /// True when the shimmer animation should play.
    /// Bind from the parent view: <c>Animate="{Binding AnimationsEnabled}"</c>.
    /// Default is true so design-time / standalone use shows the shimmer.
    /// </summary>
    public static readonly DependencyProperty AnimateProperty =
        DependencyProperty.Register(
            nameof(Animate),
            typeof(bool),
            typeof(SkeletonPanel),
            new PropertyMetadata(true, OnAnimateChanged));

    public bool Animate
    {
        get => (bool)GetValue(AnimateProperty);
        set => SetValue(AnimateProperty, value);
    }

    // ── Construction ──────────────────────────────────────────────────────────

    public SkeletonPanel()
    {
        InitializeComponent();
        Loaded   += (_, _) => ApplyAnimation();
        Unloaded += (_, _) => StopAnimation();
    }

    // ── Animation management ─────────────────────────────────────────────────

    private static void OnAnimateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SkeletonPanel panel)
            panel.ApplyAnimation();
    }

    private void ApplyAnimation()
    {
        if (!IsLoaded) return;

        if (Animate)
            StartAnimation();
        else
            StopAnimation();
    }

    private void StartAnimation()
    {
        if (_shimmerBoard is not null) return; // already running

        // Sweep from left (-120) to full width + 120 over 1.2 s, repeat forever.
        // Plain DoubleAnimation with a literal Duration — avoids the KeyTime=Duration crash trap.
        var travel = new DoubleAnimation
        {
            From           = -120,
            To             = ActualWidth + 120,
            Duration       = new Duration(System.TimeSpan.FromSeconds(1.2)),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };

        Storyboard.SetTarget(travel, ShimmerTranslate);
        Storyboard.SetTargetProperty(travel, new PropertyPath(TranslateTransform.XProperty));

        _shimmerBoard = new Storyboard();
        _shimmerBoard.Children.Add(travel);
        _shimmerBoard.Begin(this, isControllable: true);
    }

    private void StopAnimation()
    {
        if (_shimmerBoard is null) return;
        _shimmerBoard.Stop(this);
        _shimmerBoard = null;
        // Reset position so rectangle is off-screen (no leftover artefact).
        ShimmerTranslate.X = -120;
    }
}
