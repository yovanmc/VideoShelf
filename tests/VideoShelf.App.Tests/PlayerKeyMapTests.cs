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

    // ── B4: ClosePlayer enum regression tests ─────────────────────────────────
    // The B4 Esc back-out chain is:
    //   flyout open → close flyout (view-level, no keymap change)
    //   fullscreen  → ExitFullscreen (existing keymap entry)
    //   neither     → ClosePlayer (view routes there after checking state)
    // PlayerKeyMap.Resolve still returns ExitFullscreen for raw Esc — the view
    // decides context. These tests guard that ClosePlayer exists and is distinct.

    [Fact]
    public void ClosePlayer_enum_value_is_distinct_from_None_and_ExitFullscreen()
    {
        PlayerCommand.ClosePlayer.ShouldNotBe(PlayerCommand.None);
        PlayerCommand.ClosePlayer.ShouldNotBe(PlayerCommand.ExitFullscreen);
    }

    [Fact]
    public void Escape_raw_mapping_is_ExitFullscreen_not_ClosePlayer()
    {
        // The contextual Esc routing (flyout → fullscreen → close player) lives
        // in PlayerView.HandleEscapeKey, NOT in the pure keymap. This test pins
        // that the raw mapping stays ExitFullscreen so existing fullscreen tests pass.
        PlayerKeyMap.Resolve(Key.Escape, ModifierKeys.None).ShouldBe(PlayerCommand.ExitFullscreen);
    }
}
