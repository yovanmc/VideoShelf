using System.Windows.Input;

namespace VideoShelf.App.Services;

public enum PlayerCommand
{
    None,
    TogglePlayPause,
    ToggleFullscreen,
    ExitFullscreen,
    Screenshot,
    // E3: routed through the skip commands so feedback fires
    SkipBack,
    SkipForward,
}

/// <summary>Pure keyboard-to-command mapping for the player (spec §9 shortcuts).</summary>
public static class PlayerKeyMap
{
    public static PlayerCommand Resolve(Key key, ModifierKeys modifiers)
    {
        var ctrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        return (key, ctrl) switch
        {
            (Key.E, true) => PlayerCommand.Screenshot,
            (Key.Space, false) => PlayerCommand.TogglePlayPause,
            // Left/Right now route through the skip VM commands (E3: fires skip feedback)
            (Key.Left, false) => PlayerCommand.SkipBack,
            (Key.Right, false) => PlayerCommand.SkipForward,
            (Key.F, false) => PlayerCommand.ToggleFullscreen,
            (Key.Escape, false) => PlayerCommand.ExitFullscreen,
            _ => PlayerCommand.None,
        };
    }
}
