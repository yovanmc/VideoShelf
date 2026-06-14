// src/VideoShelf.App/Motion/MotionPolicy.cs
namespace VideoShelf.App.Motion;

/// <summary>Decides whether UI animations should play, honoring the OS
/// "minimize animations" setting (SystemParameters.ClientAreaAnimation) so
/// motion-sensitive users get a static UI. NOT screen-reader related.</summary>
public interface IMotionPolicy
{
    /// <summary>True when animations should play right now.</summary>
    bool ShouldAnimate { get; }
}

public sealed class SystemMotionPolicy : IMotionPolicy
{
    // ClientAreaAnimation == true means the OS permits animations.
    public bool ShouldAnimate => MotionPolicy.ShouldAnimate(
        System.Windows.SystemParameters.ClientAreaAnimation, appEnabled: true);
}

public static class MotionPolicy
{
    public static bool ShouldAnimate(bool osClientAreaAnimation, bool appEnabled)
        => osClientAreaAnimation && appEnabled;
}
