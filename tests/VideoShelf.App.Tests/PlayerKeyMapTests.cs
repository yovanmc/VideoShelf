using System.Windows.Input;
using Shouldly;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests;

public class PlayerKeyMapTests
{
    [Theory]
    [InlineData(Key.Space, ModifierKeys.None, PlayerCommand.TogglePlayPause)]
    // Left/Right now route through the VM skip commands (E3: fires skip feedback + single clamped source)
    [InlineData(Key.Left, ModifierKeys.None, PlayerCommand.SkipBack)]
    [InlineData(Key.Right, ModifierKeys.None, PlayerCommand.SkipForward)]
    [InlineData(Key.F, ModifierKeys.None, PlayerCommand.ToggleFullscreen)]
    [InlineData(Key.Escape, ModifierKeys.None, PlayerCommand.ExitFullscreen)]
    [InlineData(Key.E, ModifierKeys.Control, PlayerCommand.Screenshot)]
    public void Maps_known_keys(Key key, ModifierKeys mods, PlayerCommand expected)
        => PlayerKeyMap.Resolve(key, mods).ShouldBe(expected);

    [Fact]
    public void Unmapped_key_returns_none()
        => PlayerKeyMap.Resolve(Key.Q, ModifierKeys.None).ShouldBe(PlayerCommand.None);

    [Fact]
    public void E_without_control_is_not_screenshot()
        => PlayerKeyMap.Resolve(Key.E, ModifierKeys.None).ShouldBe(PlayerCommand.None);
}
