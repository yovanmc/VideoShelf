using System.Windows.Input;

namespace VideoShelf.App.Services;

public enum PlayerCommand
{
    None,
    TogglePlayPause,
    SeekBackward,
    SeekForward,
    ToggleFullscreen,
    ExitFullscreen,
    Screenshot,
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
            (Key.Left, false) => PlayerCommand.SeekBackward,
            (Key.Right, false) => PlayerCommand.SeekForward,
            (Key.F, false) => PlayerCommand.ToggleFullscreen,
            (Key.Escape, false) => PlayerCommand.ExitFullscreen,
            _ => PlayerCommand.None,
        };
    }
}
