# M21 — Delight & Motion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Written for Sonnet execution. If something doesn't match what this plan says — a file isn't where described, an API has a different shape, a property is already set, a binding path differs — STOP and report rather than guess.** Much of this milestone is *motion*, which is invisible to the static GDI screenshot sweep, so verification leans on unit tests + static end-states + a real-app launch smoke, not the sweep. Read the "Verification strategy" section before starting.

**Goal:** Add a tasteful, reduced-motion-aware delight layer to VideoShelf — toasts with inline Undo, skeleton loaders + thumbnail fade-in + live scan count, page/accordion/card transitions with scroll-position memory, micro-interactions (card hover, series-complete celebration, PiP snap-to-corner), and now-playing in the window titlebar — all honoring the OS "minimize animations" setting.

**Architecture:** Pure **App-layer, additive** work — no `VideoShelf.Core` change, **no schema/migration** (the M8→M20 no-`user_version`-runner streak must hold → M8→M21). One central reduced-motion gate (`MotionPolicy`, reading `SystemParameters.ClientAreaAnimation`) governs every animation: when the OS asks for reduced motion, animations resolve to their static end-state. We add animation tokens to `DesignTokens.xaml` (additive section — NO color/brush palette change, the M15/M17/M20 theming rule), a window-level toast overlay + a pure toast queue, a skeleton/shimmer control, per-view enter transitions via `IsVisibleChanged` (views are persistent panels — they are never destroyed on nav), scroll-position memory, and code-behind `BeginAnimation` for the PiP host-shrink. Delivered as **5 stacked PRs split at the Group seams** (Groups A–E), the M16/M18/M19/M20 model.

**Tech Stack:** .NET 10 WPF, WPF-UI 4.3.0 (dark-only), MVVM (CommunityToolkit.Mvvm source generators), xUnit + Shouldly (`VideoShelf.App.Tests`, **no mocking library** — hand fakes), the existing harness (`HarnessOptions` + `Run-VisualSweep.ps1`). Animations via WPF `Storyboard`/`DoubleAnimation`/`BeginAnimation` + `EasingFunctionBase`; reduced-motion via `System.Windows.SystemParameters.ClientAreaAnimation`.

---

## Locked scope (owner-decided 2026-06-14)

**IN:**
1. **Reduced-motion gate** honoring the OS "minimize animations" setting (`SystemParameters.ClientAreaAnimation`) — every animation checks it; reduced → static end-state. — Group A.
2. **Toasts / snackbars with inline Undo** as the recoverable-action surface for favorite / watchlist / bulk actions / rename / remove-source, plus a "Resumed at HH:MM" toast. — Group B.
3. **Loading & feedback:** skeleton/shimmer placeholders on the list views that render with no busy state today; scan progress with a **live count**. — Group C.
4. **Transitions:** page/directional transitions, accordion expand/collapse animation, **scroll-position memory**, and a **full shared-element card→hero morph** (owner asked to attempt it; fall back to a crossfade/scale if it fights the persistent-view hosting). — Group D.
5. **Micro-interactions:** card hover (progress reveal + elevation), series-complete celebration, **PiP snap-to-corner** + hover-fade, **now-playing in the window titlebar**. — Group D.

**CUT (owner — do NOT build):** keyboard shortcut cheat-sheet, splash + About panel, first-run tour, what's-new panel. (All other onboarding/identity items except now-playing-in-titlebar.)

**CUT from M21 by prior dedup (built in M19, do NOT rebuild):** the up-next countdown card and subtitle styling — M21 only adds *motion polish* to what M19 shipped, and the up-next card already animates acceptably; do NOT touch the M19 Up-Next logic.

> If you find yourself editing a brush, a color, `DesignTokens.xaml` palette values, `SystemColors`, or adding `AutomationProperties`/screen-reader semantics (the owner removed all screen-reader support in PR #77 — do NOT reintroduce `AutomationProperties`, UIA names, or live regions) — **STOP**, you are outside scope. The only `DesignTokens.xaml` edits allowed are *additive new animation tokens* (Durations/easings), never color changes.

---

## Verification strategy (READ FIRST — motion is invisible to the GDI sweep)

The standing screenshot sweep (`Run-VisualSweep.ps1`, GDI `CopyFromScreen`) captures **static frames** — it cannot see an animation mid-flight, and PiP/over-video motion sits behind the libVLC `VideoView` airspace limit (see M19/M20). So this milestone verifies in four layers:

1. **Pure unit tests (`VideoShelf.App.Tests`, xUnit + Shouldly, hand fakes)** for everything that can be made pure: the reduced-motion policy decision, the toast queue (enqueue/dismiss/auto-expire/undo-invoke), the scroll-position store, and the now-playing titlebar string. These are the regression gate.
2. **Static end-state screenshots** via the existing sweep — the *result* of an animation is visible even if the tween isn't: a card with the hover/elevated state forced, a toast shown (via a new harness state), a skeleton placeholder shown (via a forced `IsLoading`), the PiP snapped to the corner, the titlebar showing the now-playing title. Dispatch the sweep-reading subagent per the standing rule (text verdict, paths; never load PNGs into the controller).
3. **Real-app launch smoke** with `--done-signal` on a populated library after each group (the M17 crash-on-launch / M20 lesson) — confirm no XAML/startup regression from the new resources/overlays.
4. **A flagged manual owner gate** at milestone end: watch the toasts appear+undo, the page/card transitions, the series-complete moment, the PiP snap, and confirm reduced-motion (toggle Windows "Show animations in Windows" off) disables them. Note this in the PR bodies.

**Gate = green build + all tests pass + the sweep subagent verdict PASS on the new static end-states + a clean real-app smoke**, not the test count. **Baseline before M21: 915 tests** (368 Core + 547 App).

---

## File structure (what gets created / modified)

**Created:**
- `src/VideoShelf.App/Motion/MotionPolicy.cs` — the reduced-motion gate (`IMotionPolicy` + `SystemMotionPolicy` + a pure `static bool ShouldAnimate(bool osClientAreaAnimation, bool appEnabled)`).
- `src/VideoShelf.App/Motion/Toast.cs` — the toast model (`record Toast`) + `ToastKind` enum.
- `src/VideoShelf.App/Motion/ToastService.cs` + `IToastService.cs` — the pure-ish toast queue/host VM (`ObservableCollection<ToastViewModel>` + show/dismiss/undo; auto-dismiss via an injected timer seam so it's unit-testable).
- `src/VideoShelf.App/ViewModels/ToastViewModel.cs` — one toast's VM (message, optional Undo command, kind).
- `src/VideoShelf.App/Views/Controls/ToastHost.xaml` (+ `.xaml.cs`) — the window-level overlay that renders the toast stack.
- `src/VideoShelf.App/Views/Controls/SkeletonPanel.xaml` (+ `.xaml.cs`) — a reusable shimmer/skeleton placeholder control (reduced-motion aware).
- `src/VideoShelf.App/Motion/ScrollMemory.cs` — attached behavior that remembers/restores a `ScrollViewer` offset keyed by `AppView`.
- `src/VideoShelf.App/Motion/ViewTransition.cs` — attached behavior that plays an enter transition when a persistent view becomes visible (`IsVisibleChanged`), reduced-motion aware.
- `src/VideoShelf.App/Motion/HeroTransition.cs` (+ helpers) — the shared-element card→hero morph (ATTEMPT; see Task D4 for the fallback).
- Tests under `tests/VideoShelf.App.Tests/Motion/`: `MotionPolicyTests.cs`, `ToastServiceTests.cs`, `ScrollMemoryTests.cs`, `NowPlayingTitleTests.cs`.

**Modified (additive only):**
- `src/VideoShelf.App/Resources/DesignTokens.xaml` — **additive only**: an `<!-- ANIMATION TOKENS -->` section (Durations `AnimFast`/`AnimNormal`/`AnimSlow`, an `CubicEase` easing). **No color/brush edits.**
- `src/VideoShelf.App/Views/VideoCard.xaml`, `CreatorCard.xaml` — thumbnail fade-in (apply existing `ThumbnailImage` style + `NotifyOnTargetUpdated`), hover elevation + progress reveal, the hero-transition hook (D4).
- `src/VideoShelf.App/Views/MainWindow.xaml` (+ `.xaml.cs`) — host the `ToastHost` overlay (sibling of the command-palette overlay), bind `FluentWindow`/`TitleBar` Title to now-playing, PiP host-shrink animation, the hero-transition overlay layer.
- `src/VideoShelf.App/ViewModels/MainViewModel.cs` — own the `IToastService`; raise toasts on resume; flip an `IsLoading`-style flag where lists load; expose the now-playing title string; reduced-motion-aware PiP toggle.
- The list views (`FavoritesView.xaml`, `WatchlistView.xaml`, `HistoryView.xaml`, `PlaylistsView.xaml`, `SmartViewsView.xaml`, `MainWindow.xaml` Browse grid) — skeleton overlay gated by an `IsLoading` flag on each VM.
- `src/VideoShelf.App/ViewModels/EpisodeViewModel.cs`, `BulkActionBarViewModel.cs`, `SourcesViewModel.cs`, `RenameToolViewModel.cs`, `MultiRenameViewModel.cs` — route their undoable actions through `IToastService` (additive — keep existing persistent Undo buttons as a fallback).
- `src/VideoShelf.App/Views/SettingsView.xaml`, `MaintenanceView.xaml` — scan progress live count.
- `src/VideoShelf.App/Views/SectionDetailView.xaml` — accordion expand/collapse animation; the hero-transition target (series tile → could open detail).
- `src/VideoShelf.App/Harness/HarnessOptions.cs` + `HarnessRunner.cs` — new `--view` states for toast/skeleton/series-complete/PiP-snap capture.
- `tools/harness/Run-VisualSweep.ps1` — add the new view states to the sweep list.
- DI registration (`src/VideoShelf.App/Services/ServiceCollectionExtensions.cs`) — register `IMotionPolicy`, `IToastService`.

---

# GROUP A — Motion foundation + quick wins (PR #1)

**Branch:** `feat/m21-motion-foundation`. **Outcome:** a unit-tested reduced-motion gate, additive animation tokens, thumbnail fade-in on both card types, and card hover micro-interaction — all reduced-motion aware. This is the foundation every later group depends on.

> Setup (start of group, from the runbook §3):
> ```bash
> cd "C:/Agent Projects/VideoShelf" && git checkout main && git pull
> cd "C:/Agent Projects/VideoShelf" && git worktree add ".worktrees/feat-m21-motion-foundation" -b "feat/m21-motion-foundation"
> cd "C:/Agent Projects/VideoShelf/.worktrees/feat-m21-motion-foundation" && dotnet test VideoShelf.slnx -c Release --nologo -v q 2>&1 | tail -5
> ```
> Baseline MUST be green (915 tests). If red, STOP and report.

### Task A1: `MotionPolicy` — the reduced-motion gate (pure, unit-tested)

**Files:**
- Create: `src/VideoShelf.App/Motion/MotionPolicy.cs`
- Test: `tests/VideoShelf.App.Tests/Motion/MotionPolicyTests.cs`
- Modify: DI registration in `src/VideoShelf.App/Services/ServiceCollectionExtensions.cs`

The decision "should this animation run?" must be pure so it's testable; the OS read happens behind an interface.

- [ ] **Step 1: Write the failing test.**

```csharp
// tests/VideoShelf.App.Tests/Motion/MotionPolicyTests.cs
using VideoShelf.App.Motion;
using Xunit;
using Shouldly;

public class MotionPolicyTests
{
    [Theory]
    [InlineData(true,  true,  true)]   // OS allows + app enabled -> animate
    [InlineData(false, true,  false)]  // OS minimize-animations -> no
    [InlineData(true,  false, false)]  // app disabled -> no
    [InlineData(false, false, false)]
    public void ShouldAnimate_respects_os_and_app(bool osAnim, bool appEnabled, bool expected)
        => MotionPolicy.ShouldAnimate(osAnim, appEnabled).ShouldBe(expected);
}
```

- [ ] **Step 2: Run, confirm fail.** Run: `dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q --filter MotionPolicyTests`. Expected: type not found.

- [ ] **Step 3: Implement.**

```csharp
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
```

- [ ] **Step 4: Run tests, confirm pass** (filtered). Expected: 4 passing.

- [ ] **Step 5: Register in DI** as a singleton in `ServiceCollectionExtensions.cs` (match the existing `services.AddSingleton<IConfirmService, ConfirmService>()` idiom): `services.AddSingleton<IMotionPolicy, SystemMotionPolicy>();`. Build clean (`dotnet build VideoShelf.slnx -c Release -v minimal 2>&1 | tail -5`).

- [ ] **Step 6: Commit.**

```bash
git add src/VideoShelf.App/Motion/MotionPolicy.cs tests/VideoShelf.App.Tests/Motion/MotionPolicyTests.cs src/VideoShelf.App/Services/ServiceCollectionExtensions.cs
git commit -m "feat(motion): add reduced-motion gate (MotionPolicy)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task A2: Animation tokens in DesignTokens.xaml

**Files:** Modify `src/VideoShelf.App/Resources/DesignTokens.xaml` (the IMAGE section at the bottom already holds the one existing Storyboard — add an ANIMATION TOKENS block near it).

- [ ] **Step 1: Add the additive tokens** (NO color/brush change):

```xml
<!-- ANIMATION TOKENS (additive — M21) -->
<Duration x:Key="AnimFast">0:0:0.12</Duration>
<Duration x:Key="AnimNormal">0:0:0.22</Duration>
<Duration x:Key="AnimSlow">0:0:0.35</Duration>
<CubicEase x:Key="EaseOut" EasingMode="EaseOut"/>
<CubicEase x:Key="EaseInOut" EasingMode="EaseInOut"/>
```

- [ ] **Step 2: Build.** Run: `dotnet build VideoShelf.slnx -c Release -v minimal 2>&1 | tail -5`. Expected: 0 errors. (A bad `Duration`/easing key fails XAML parse at build for resources used at compile time, or at runtime — if the build is clean, smoke-launch in A5 confirms parse.)

- [ ] **Step 3: Commit** (`feat(motion): add additive animation duration/easing tokens`).

### Task A3: Thumbnail fade-in on cards

**Files:** Modify `src/VideoShelf.App/Views/VideoCard.xaml`, `src/VideoShelf.App/Views/CreatorCard.xaml`.

The `ThumbnailImage` style (already in `DesignTokens.xaml`, a 250ms fade on `Binding.TargetUpdated`) is not applied to the card images. Apply it.

- [ ] **Step 1: VideoCard** — on the thumbnail `<Image>` (inside the clip `Border`), add `Style="{StaticResource ThumbnailImage}"` and change its source binding to `Source="{Binding ThumbnailPath, NotifyOnTargetUpdated=True}"` (the style's `EventTrigger` listens for `Binding.TargetUpdated`, which requires `NotifyOnTargetUpdated=True`). Keep `Stretch="UniformToFill"`.

- [ ] **Step 2: CreatorCard** — same on its `<Image>`: add `Style="{StaticResource ThumbnailImage}"`, and on the `Source="{Binding ImagePath, IsAsync=True}"` binding add `NotifyOnTargetUpdated=True`. Keep `Stretch="UniformToFill"`.

> Note: the existing `ThumbnailImage` style is a fixed 250ms fade and is NOT yet reduced-motion gated. That's acceptable for a one-shot image fade-in (it's not a looping/large-movement animation). Do NOT try to gate it per-instance here — keep this task additive and simple. If the owner later objects, the style itself can be gated centrally.

- [ ] **Step 3: Build + smoke.** Build clean. (Visible result verified in A5 sweep.)

- [ ] **Step 4: Commit** (`feat(motion): fade thumbnails in on video and creator cards`).

### Task A4: Card hover micro-interaction (elevation + progress reveal)

**Files:** Modify `src/VideoShelf.App/Views/VideoCard.xaml` (and `CreatorCard.xaml` for elevation only — it has no progress).

Cards have no hover affordance today (only the WPF-UI button press state). Add a subtle scale/elevation on hover and reveal the progress % on hover for VideoCard. Use a `VisualStateManager` or `Style.Triggers` with `IsMouseOver` on the root `Button`; animate `RenderTransform` scale (1.0→1.03) + a `DropShadowEffect`/opacity. Reduced-motion: the hover *state* (showing the % / elevation) is fine to keep, but the *animated tween* should be near-instant under reduced motion — simplest correct approach: keep the trigger setters but make the `Storyboard` durations bound to a tokened duration; under reduced motion the VSM/transition still settles to the same end-state, just without a visible tween. For this task, a `Trigger`-based approach (no explicit Storyboard) snaps to the end-state and is inherently reduced-motion-safe; prefer it for the elevation, and use a short `EventTrigger` Storyboard only for the scale.

- [ ] **Step 1: VideoCard hover** — on the root `<Button>`, add a `RenderTransform` of a `ScaleTransform` (set `RenderTransformOrigin="0.5,0.5"`) and `Style.Triggers`/`ControlTemplate` triggers, OR add to the card a `Border` wrapper with triggers. Concretely, wrap the card content `StackPanel` is already inside the Button; add:

```xml
<!-- on the root Button -->
<Button.RenderTransform>
    <ScaleTransform x:Name="CardScale" ScaleX="1" ScaleY="1"/>
</Button.RenderTransform>
<Button.RenderTransformOrigin>0.5,0.5</Button.RenderTransformOrigin>
<Button.Style>
    <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Trigger.EnterActions>
                    <BeginStoryboard>
                        <Storyboard>
                            <DoubleAnimation Storyboard.TargetName="CardScale" Storyboard.TargetProperty="ScaleX"
                                             To="1.03" Duration="{StaticResource AnimFast}"/>
                            <DoubleAnimation Storyboard.TargetName="CardScale" Storyboard.TargetProperty="ScaleY"
                                             To="1.03" Duration="{StaticResource AnimFast}"/>
                        </Storyboard>
                    </BeginStoryboard>
                </Trigger.EnterActions>
                <Trigger.ExitActions>
                    <BeginStoryboard>
                        <Storyboard>
                            <DoubleAnimation Storyboard.TargetName="CardScale" Storyboard.TargetProperty="ScaleX"
                                             To="1" Duration="{StaticResource AnimFast}"/>
                            <DoubleAnimation Storyboard.TargetName="CardScale" Storyboard.TargetProperty="ScaleY"
                                             To="1" Duration="{StaticResource AnimFast}"/>
                        </Storyboard>
                    </BeginStoryboard>
                </Trigger.ExitActions>
            </Trigger>
        </Style.Triggers>
    </Style>
</Button.Style>
```

> If the card Button already has a `Style`/`BasedOn` (the digest says it uses the WPF-UI default Button + `FocusVisualStyle="{StaticResource AppFocusVisual}"`), MERGE these triggers into the existing style rather than overwriting it. **Do not remove `FocusVisualStyle`, `IsTabStop`, or any kept-from-M20 attribute.** If merging is awkward, STOP and report.

- [ ] **Step 2: Progress reveal on hover (VideoCard)** — the progress % `TextBlock` (gated by `HasProgress`) should be more prominent on hover. Keep it simple: bind its `Opacity` so it's e.g. 0.6 normally and 1.0 when the card `IsMouseOver` (a `DataTrigger`/`Trigger` on the parent Button's `IsMouseOver`). Additive; do not remove the existing `HasProgress` visibility gate.

- [ ] **Step 3: CreatorCard** — apply the same scale-on-hover `Style.Triggers` block (no progress reveal).

- [ ] **Step 4: Reduced-motion note.** The scale Storyboards use `AnimFast` (120ms) and are tiny one-shot tweens; under OS reduced-motion they still play. If the owner wants them fully suppressed, a later refinement can route them through `MotionPolicy`; **for this task do NOT add per-card policy plumbing** (it would require code-behind on a UserControl per instance — out of proportion). Document this in the PR.

- [ ] **Step 5: Build + commit** (`feat(motion): card hover elevation and progress reveal`).

### Task A5: Group A sweep + smoke + finish PR #1

- [ ] **Step 1:** Full test gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q`. Expected 919 (915 + 4 MotionPolicy). 0 failures (re-run the known `OrphanCleanupTests` ordering flake once if it appears).
- [ ] **Step 2: Real-app smoke** — launch with `--view Browse --seed-demo --done-signal <sig>` (mirror `Run-VisualSweep.ps1`'s launch line) on the fixtures; confirm a clean launch + done-signal (the new resources/tokens parse). Report the command.
- [ ] **Step 3: Sweep** — run `Run-VisualSweep.ps1` for Home/Browse; dispatch the sweep-reading Sonnet subagent: do cards render correctly (thumbnails present, no layout break from the hover transform/scale origin)? PASS/FAIL + paths.
- [ ] **Step 4:** Finish per runbook §5 — push, PR `M21 Group A — motion foundation + card polish`, watch CI green, merge `--merge --delete-branch` from the **main repo root**, clean up the worktree, pull main. Do **not** flip the ROADMAP row yet (flip on the final PR). Proceed to Group B.

---

# GROUP B — Toasts + inline Undo (PR #2)

**Branch:** `feat/m21-toasts` (rebased on freshly-merged main). **Outcome:** a window-level toast overlay + a unit-tested toast queue; favorite/watchlist/bulk/rename/remove-source actions raise an undoable toast; a "Resumed at HH:MM" toast on resume.

### Task B1: Toast model + service (pure queue, unit-tested)

**Files:**
- Create: `src/VideoShelf.App/Motion/Toast.cs`, `src/VideoShelf.App/Motion/IToastService.cs`, `src/VideoShelf.App/Motion/ToastService.cs`, `src/VideoShelf.App/ViewModels/ToastViewModel.cs`
- Test: `tests/VideoShelf.App.Tests/Motion/ToastServiceTests.cs`
- Modify: DI registration.

Design: `ToastService` exposes an `ObservableCollection<ToastViewModel> Toasts` the overlay binds to. `Show(message, undo?)` adds one; it auto-dismisses after a delay via an **injected timer seam** (`Action<TimeSpan, Action> scheduleDismiss`) so tests can run the dismiss synchronously without a real clock. Undo invokes the supplied callback then dismisses.

- [ ] **Step 1: Write the failing test.**

```csharp
// tests/VideoShelf.App.Tests/Motion/ToastServiceTests.cs
using System;
using System.Collections.Generic;
using VideoShelf.App.Motion;
using Xunit;
using Shouldly;

public class ToastServiceTests
{
    // Capture the scheduled dismiss so we can fire it manually.
    private static (ToastService svc, List<Action> pending) Make()
    {
        var pending = new List<Action>();
        var svc = new ToastService((delay, act) => pending.Add(act));
        return (svc, pending);
    }

    [Fact]
    public void Show_adds_a_toast()
    {
        var (svc, _) = Make();
        svc.Show("Marked watched");
        svc.Toasts.Count.ShouldBe(1);
        svc.Toasts[0].Message.ShouldBe("Marked watched");
    }

    [Fact]
    public void Auto_dismiss_removes_the_toast()
    {
        var (svc, pending) = Make();
        svc.Show("Hi");
        pending[0].Invoke();          // simulate the timer firing
        svc.Toasts.ShouldBeEmpty();
    }

    [Fact]
    public void Undo_invokes_callback_and_dismisses()
    {
        var (svc, _) = Make();
        var undone = false;
        svc.Show("Removed source", undo: () => undone = true);
        svc.Toasts[0].UndoCommand!.Execute(null);
        undone.ShouldBeTrue();
        svc.Toasts.ShouldBeEmpty();
    }

    [Fact]
    public void Toast_without_undo_has_no_undo_command()
    {
        var (svc, _) = Make();
        svc.Show("Scan complete");
        svc.Toasts[0].UndoCommand.ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run, confirm fail** (`--filter ToastServiceTests`).

- [ ] **Step 3: Implement.**

```csharp
// src/VideoShelf.App/Motion/Toast.cs
namespace VideoShelf.App.Motion;
public enum ToastKind { Info, Success, Warning }
```

```csharp
// src/VideoShelf.App/Motion/IToastService.cs
using System;
namespace VideoShelf.App.Motion;
public interface IToastService
{
    System.Collections.ObjectModel.ObservableCollection<VideoShelf.App.ViewModels.ToastViewModel> Toasts { get; }
    void Show(string message, Action? undo = null, ToastKind kind = ToastKind.Info);
    void Dismiss(VideoShelf.App.ViewModels.ToastViewModel toast);
}
```

```csharp
// src/VideoShelf.App/ViewModels/ToastViewModel.cs
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.Motion;

namespace VideoShelf.App.ViewModels;

public sealed partial class ToastViewModel : ObservableObject
{
    public string Message { get; }
    public ToastKind Kind { get; }
    public ICommand? UndoCommand { get; }

    public ToastViewModel(string message, ToastKind kind, ICommand? undoCommand)
    {
        Message = message;
        Kind = kind;
        UndoCommand = undoCommand;
    }
}
```

```csharp
// src/VideoShelf.App/Motion/ToastService.cs
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Motion;

public sealed class ToastService : IToastService
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);
    private readonly Action<TimeSpan, Action> _scheduleDismiss;

    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    /// <param name="scheduleDismiss">Seam: schedules <paramref name="scheduleDismiss"/>'s
    /// action after the delay. Production passes a DispatcherTimer-backed scheduler;
    /// tests capture and fire it synchronously.</param>
    public ToastService(Action<TimeSpan, Action> scheduleDismiss)
        => _scheduleDismiss = scheduleDismiss;

    public void Show(string message, Action? undo = null, ToastKind kind = ToastKind.Info)
    {
        ToastViewModel toast = null!;
        RelayCommand? undoCmd = undo is null ? null : new RelayCommand(() =>
        {
            undo();
            Dismiss(toast);
        });
        toast = new ToastViewModel(message, kind, undoCmd);
        Toasts.Add(toast);
        _scheduleDismiss(DefaultDuration, () => Dismiss(toast));
    }

    public void Dismiss(ToastViewModel toast)
    {
        if (Toasts.Contains(toast)) Toasts.Remove(toast);
    }
}
```

- [ ] **Step 4: Run tests, confirm pass** (4 passing).

- [ ] **Step 5: Register in DI** — the production scheduler uses a `DispatcherTimer` (one-shot). In `ServiceCollectionExtensions.cs`, register:

```csharp
services.AddSingleton<IToastService>(_ => new ToastService((delay, act) =>
{
    var timer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
    timer.Tick += (_, _) => { timer.Stop(); act(); };
    timer.Start();
}));
```

Build clean.

- [ ] **Step 6: Commit** (`feat(toast): add toast service + queue with inline undo`).

### Task B2: ToastHost overlay

**Files:** Create `src/VideoShelf.App/Views/Controls/ToastHost.xaml` (+ `.xaml.cs`); modify `src/VideoShelf.App/Views/MainWindow.xaml` to host it; expose `IToastService` on `MainViewModel`.

- [ ] **Step 1: Expose the service on the VM.** In `MainViewModel.cs`, inject `IToastService` (nullable-trailing-param idiom to avoid ctor fan-out — `IToastService? toasts = null`, store it; if null in a test, create a no-op `new ToastService((_, _) => {})`). Expose `public IToastService Toasts => _toasts;`.

- [ ] **Step 2: Build the overlay control.** `ToastHost` is a bottom-right vertical stack bound to `Toasts.Toasts`, each toast a rounded `Border` (use existing tokens — `ChipFillBrush` or a surface brush, NO new color) with the message + an optional "Undo" `ui:Button` (visible when `UndoCommand != null`, via `BoolToVisibility`/a null converter that already exists — the digest lists no null converter, so bind the Undo button `Visibility` using the existing pattern: add a tiny `ToastViewModel.HasUndo => UndoCommand != null` bool and bind `BoolToVisibility`). Entry animation: a slide-up + fade `EventTrigger` on `Loaded` of each item container, durations from `AnimNormal`, **gated**: read `IMotionPolicy` — simplest is to expose `MainViewModel.Toasts` plus a `bool AnimationsEnabled => _motion.ShouldAnimate` on `MainViewModel` and bind the entry Storyboard's `BeginTime`/skip via that; OR keep the entry animation unconditional (a 220ms fade is mild) and document it. Prefer: bind nothing fancy — a `Loaded` `EventTrigger` fade from 0→1 opacity is acceptable and mild; leave reduced-motion suppression of the *toast entry* as documented-acceptable (the toast still appears, just without the slide). Do NOT block the toast from showing under reduced motion.

```xml
<!-- ToastHost.xaml (UserControl) -->
<ItemsControl ItemsSource="{Binding Toasts.Toasts}" VerticalAlignment="Bottom" HorizontalAlignment="Right" Margin="0,0,24,24">
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <Border CornerRadius="8" Background="{StaticResource ChipFillBrush}" Padding="14,10" Margin="0,8,0,0" MaxWidth="360">
        <StackPanel Orientation="Horizontal">
          <TextBlock Text="{Binding Message}" VerticalAlignment="Center" TextWrapping="Wrap"/>
          <ui:Button Content="Undo" Margin="14,0,0,0" Command="{Binding UndoCommand}"
                     Visibility="{Binding HasUndo, Converter={StaticResource BoolToVisibility}}"/>
        </StackPanel>
      </Border>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

Add `ToastViewModel.HasUndo => UndoCommand != null`.

- [ ] **Step 3: Host it in MainWindow.** Place `<controls:ToastHost DataContext="{Binding}"/>` (or bind its needed context) as the TOP-MOST sibling in the root `Grid` of `MainWindow.xaml` (after the command-palette overlay, so toasts float above everything). It must NOT capture input outside its toasts (`Background="Transparent"`, `IsHitTestVisible` only on the toast borders — an `ItemsControl` with no background is already click-through except its children). Confirm clicks pass through to the app.

- [ ] **Step 4: Build + smoke.** Build clean; launch smoke — no crash. (Visible state captured via a harness toast state in Group E; for now confirm it builds + launches.)

- [ ] **Step 5: Commit** (`feat(toast): window-level toast overlay`).

### Task B3: Route undoable actions through toasts (+ Resumed toast)

**Files:** Modify `MainViewModel.cs` (resume toast + a helper), `EpisodeViewModel.cs` (favorite/watchlist), `BulkActionBarViewModel.cs` (bulk), `SourcesViewModel.cs` (remove-source), `RenameToolViewModel.cs`/`MultiRenameViewModel.cs` (rename). **Additive — keep existing persistent Undo buttons.**

> The VMs that need to raise toasts must reach `IToastService`. Thread it via the same nullable-trailing-param idiom used for the M16 repos / M20 `IConfirmService`. Where a VM is constructed deep in a chain (Episode/Series), pass the service down the existing chain (the digest shows Episode/Series VMs already take optional trailing deps). If a VM has no clean construction seam to receive `IToastService`, STOP and report rather than forcing it.

- [ ] **Step 1: Favorite toggle (EpisodeViewModel).** After `ToggleFavoriteCommand` flips + persists, show a toast with an Undo that toggles back:

```csharp
_toasts?.Show(IsFavorite ? "Added to favorites" : "Removed from favorites",
              undo: () => ToggleFavoriteCommand.Execute(null), ToastKind.Success);
```

Same for `ToggleWatchlistCommand` ("Added to watchlist"/"Removed from watchlist"). (These are pure toggles, so Undo = run the command again.)

- [ ] **Step 2: Bulk actions (BulkActionBarViewModel).** After `AddFavoriteCommand`/`RemoveFavoriteCommand`/`MarkWatchedCommand`/etc. complete, snapshot the affected `VideoIds` and show a toast whose Undo runs the inverse command over the same snapshot, e.g.:

```csharp
var ids = VideoIds.ToList();
_toasts?.Show($"Marked {ids.Count} watched",
              undo: () => MarkUnwatched(ids), ToastKind.Success);
```

Where `MarkUnwatched(IReadOnlyList<long>)` is the existing inverse loop (extract a private method if the command currently only loops over `VideoIds`). For `AddToQueue`/`ApplyTag` (no simple inverse today) show a toast **without** Undo (informational). Do NOT invent a bulk remove-tag for Undo here (out of scope).

- [ ] **Step 3: Remove-source (SourcesViewModel).** After `RemoveSourceCommand` removes, show a toast: `_toasts?.Show("Source removed", undo: () => UndoRemoveCommand.Execute(null), ToastKind.Warning);`. Keep the existing persistent "Undo remove source" button too (fallback). The toast and the button both call the same `UndoRemoveCommand`.

- [ ] **Step 4: Rename applied (RenameTool/MultiRename).** After a successful `Apply`, show `_toasts?.Show($"Renamed {result.Renamed}", undo: () => UndoCommand.Execute(null));` (use the actual count property on the rename result). Keep the existing "Undo last rename" button.

- [ ] **Step 5: Resumed toast (MainViewModel).** When playback resumes from a saved position (find where resume is applied/offered in `MainViewModel`/`PlayerViewModel` — the resume-offer path), show `_toasts?.Show($"Resumed at {position:hh\\:mm\\:ss}");` (informational, no Undo). Use the existing resume position value; format `TimeSpan`. If the resume happens in `PlayerViewModel` (which may not have `IToastService`), raise it from `MainViewModel.OpenPlayer` after `_player.Open(...)` when a resume position exists, to keep the dependency in `MainViewModel`.

- [ ] **Step 6: Unit test the bulk inverse extraction** (if you extracted `MarkUnwatched(ids)`): a quick `BulkActionBarViewModel` test that calls the command then the inverse and asserts DB state round-trips (use `AppTempDb` + real repos, the established pattern). Run it.

- [ ] **Step 7: Build + full test gate.** Expected 919 + the bulk test. Commit (`feat(toast): raise undoable toasts for favorite/watchlist/bulk/rename/remove-source + resume`).

### Task B4: Harness toast state + Group B finish PR #2

- [ ] **Step 1: Harness state.** Add a `--view FavoriteToast` (or `Toast`) case in `HarnessRunner.NavigateAsync` that, via `_postSettleAction`, calls `_main.Toasts.Show("Added to favorites", undo: () => {})` so the sweep can capture a toast. Add the option exactly like the existing player sub-states (set `_postSettleAction`, `isPlayerState=false`).
- [ ] **Step 2:** Full test gate green. Real-app smoke. Sweep the toast state via the new `--view`; dispatch the sweep subagent: is a toast visibly rendered bottom-right with an Undo button? PASS/FAIL.
- [ ] **Step 3:** Whole-branch review (runbook §4 step F), address blockers.
- [ ] **Step 4:** Push, PR `M21 Group B — toasts + inline undo`, CI green, merge from main root, clean up. Do not flip ROADMAP. Proceed to Group C.

---

# GROUP C — Loading & feedback (PR #3)

**Branch:** `feat/m21-loading-feedback` (rebased on merged main). **Outcome:** skeleton/shimmer placeholders on list views that load with no busy state today; scan progress with a live count.

### Task C1: SkeletonPanel control + IsLoading states

**Files:** Create `src/VideoShelf.App/Views/Controls/SkeletonPanel.xaml` (+ `.xaml.cs`); modify the list views + their VMs to expose `IsLoading`.

- [ ] **Step 1: Build the skeleton control.** `SkeletonPanel` shows N placeholder card rectangles (rounded `Border`s using `ThumbPlaceholderBrush` — the existing token) in a wrap layout. A shimmer = a looping gradient sweep `Storyboard`. **Reduced-motion:** the shimmer Storyboard must be suppressed when motion is off — give `SkeletonPanel` a `DependencyProperty bool Animate` (default true) set from binding `{Binding AnimationsEnabled}` (add `MainViewModel.AnimationsEnabled => _motion.ShouldAnimate`), and gate the shimmer `BeginStoryboard` on it via a `DataTrigger`; when off, show static placeholder rectangles (no sweep). Keep it simple — a handful of static rounded `Border`s is the baseline; the shimmer sweep is the enhancement.

- [ ] **Step 2: Add `IsLoading` to one list VM first (Favorites).** In `FavoritesViewModel`, add `[ObservableProperty] private bool _isLoading;` set `true` at the start of `LoadAsync` and `false` in a `finally`. In `FavoritesView.xaml`, overlay a `SkeletonPanel` gated `Visibility="{Binding IsLoading, Converter={StaticResource BoolToVisibility}}"` above the list. Verify the list still renders when not loading.

- [ ] **Step 3: Repeat for the other no-busy-state list VMs** — `WatchlistViewModel`, `HistoryViewModel`, `PlaylistsViewModel`, `SmartViewsViewModel`, and the Browse creator grid (`CreatorsViewModel`/wherever Browse loads). Same `IsLoading` + `SkeletonPanel` overlay pattern. If any of these loads synchronously so fast that `IsLoading` never visibly flips, that's fine — the skeleton is correctness-harmless; do not add artificial delays.

- [ ] **Step 4: Build + test gate.** Add no new unit tests unless you extract a pure helper (none needed). Build clean.

- [ ] **Step 5: Commit** (`feat(motion): skeleton loaders on list views (reduced-motion aware)`).

### Task C2: Scan progress with a live count

**Files:** Modify `MainViewModel.cs` (or `IScanCoordinator`) to expose a live count string; `SettingsView.xaml` + `MaintenanceView.xaml` to show it next to the spinner.

- [ ] **Step 1: Surface a count.** The scan pipeline already produces a `ScanResult(Added/Updated/Restored/Missing)` (M18). During the scan, surface an incremental status string (e.g. `ScanStatusText` — "Scanning… 42 found"). If the scan loop doesn't expose progress mid-run (only a final result), the minimal honest version is a phase label ("Scanning library…" → "Probing durations…" → done with the final diff) rather than a fake number. **Do NOT fabricate a count** — if no real incremental count is available, show the phase + the final `last_scan_summary` count (which already exists). Inspect `ScanService`/`MediaBackfillService` for any progress callback; if none, use phase labels + final count and note it (no silent fake).

- [ ] **Step 2: Bind it.** In `SettingsView.xaml` next to the existing `ui:ProgressRing` (bound to `IsScanning`), add a `TextBlock Text="{Binding ScanStatusText}"` visible while `IsScanning`. Same on `MaintenanceView.xaml`.

- [ ] **Step 3: Build + commit** (`feat(motion): live scan progress text`).

### Task C3: Group C finish PR #3

- [ ] **Step 1:** Full test gate green; real-app smoke.
- [ ] **Step 2:** Sweep a list view with `IsLoading` forced (add a `--view FavoritesLoading` harness state that sets `IsLoading=true` via `_postSettleAction` and does NOT clear it) — sweep subagent: is a skeleton placeholder visibly rendered? PASS/FAIL.
- [ ] **Step 3:** Whole-branch review; push, PR `M21 Group C — skeleton loaders + live scan progress`, CI green, merge from main root, clean up. Proceed to Group D.

---

# GROUP D — Transitions + delight (PR #4)

**Branch:** `feat/m21-transitions` (rebased on merged main). **Outcome:** page/accordion transitions, scroll-position memory, series-complete celebration, PiP snap-to-corner + hover-fade, now-playing in the titlebar, and the shared-element card→hero morph (with crossfade fallback).

### Task D1: Per-view enter transition (reduced-motion aware)

**Files:** Create `src/VideoShelf.App/Motion/ViewTransition.cs`; apply it in `MainWindow.xaml` to each persistent view host.

Views are persistent panels toggled by `Visibility` (never destroyed), so `Loaded` won't re-fire on nav. Use `IsVisibleChanged`: when a view becomes visible, play a short fade+translate enter. The behavior reads `IMotionPolicy` (passed via a static accessor or an attached DP bound to `AnimationsEnabled`); under reduced motion it sets the end-state immediately.

- [ ] **Step 1: Implement the attached behavior.**

```csharp
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
```

- [ ] **Step 2: Wire `ShouldAnimate` from DI** — in `MainWindow` code-behind (or `App` startup) after the container is built: `ViewTransition.ShouldAnimate = () => motionPolicy.ShouldAnimate;` (resolve `IMotionPolicy` from the same container the window uses). If the window can't reach the container cleanly, set it in `App.xaml.cs` where DI is composed. STOP and report if there's no clean access point.

- [ ] **Step 3: Apply** `motion:ViewTransition.Enabled="True"` (add the `xmlns:motion="clr-namespace:VideoShelf.App.Motion"`) on each top-level view UserControl in `MainWindow.xaml` (Home/Browse/SectionDetail/Settings/etc.). Do NOT apply it to the player host (over-video airspace).

- [ ] **Step 4: Build + smoke.** Build clean; launch smoke. Commit (`feat(motion): per-view enter transitions (reduced-motion aware)`).

### Task D2: Scroll-position memory

**Files:** Create `src/VideoShelf.App/Motion/ScrollMemory.cs`; test `tests/VideoShelf.App.Tests/Motion/ScrollMemoryTests.cs`; apply on the scrollable views.

Persistent views keep their `ScrollViewer` offset already (they're not destroyed) — so the main gap is restoring a *list's* scroll when navigating Back into a detail-and-back flow where the content reloads. Provide a pure store keyed by `AppView` that remembers an offset, with the wiring saving on navigate-away and restoring on navigate-back.

- [ ] **Step 1: Write the failing test** for the pure store.

```csharp
// tests/VideoShelf.App.Tests/Motion/ScrollMemoryTests.cs
using VideoShelf.App.Motion;
using VideoShelf.App.ViewModels;   // AppView
using Xunit; using Shouldly;

public class ScrollMemoryTests
{
    [Fact]
    public void Remembers_and_returns_offset_per_view()
    {
        var store = new ScrollOffsetStore();
        store.Save(AppView.Browse, 250);
        store.TryGet(AppView.Browse, out var y).ShouldBeTrue();
        y.ShouldBe(250);
    }

    [Fact]
    public void Unknown_view_returns_false()
    {
        var store = new ScrollOffsetStore();
        store.TryGet(AppView.Home, out _).ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run, confirm fail.**

- [ ] **Step 3: Implement the store** (pure) + an attached behavior that uses it.

```csharp
// src/VideoShelf.App/Motion/ScrollMemory.cs
using System.Collections.Generic;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Motion;

public sealed class ScrollOffsetStore
{
    private readonly Dictionary<AppView, double> _offsets = new();
    public void Save(AppView view, double y) => _offsets[view] = y;
    public bool TryGet(AppView view, out double y) => _offsets.TryGetValue(view, out y);
}
```

(The attached-behavior wiring that saves on `ScrollChanged`/navigate-away and restores via `scrollViewer.ScrollToVerticalOffset` keyed by the current `AppView` is App-glue; keep it minimal and in this file. If wiring it cleanly into the persistent-view model is awkward, the pure store + a single Browse-grid application is the acceptable minimum — document any view not covered, no silent omission.)

- [ ] **Step 4: Run tests, confirm pass. Apply** to at least the Browse grid + one secondary list. Build + commit (`feat(motion): scroll-position memory per view`).

### Task D3: Accordion expand/collapse animation

**Files:** Modify `src/VideoShelf.App/Views/SectionDetailView.xaml` (the series-grid accordion that expands episodes in place — M9/M17).

- [ ] **Step 1:** The expanded episode panel currently appears/disappears instantly (`Visibility`). Animate its `Height`/`Opacity` on expand via a `Storyboard` triggered by the `IsExpanded` data state. Because animating `Height="Auto"` is not directly possible, animate `Opacity` + a `ScaleTransform.ScaleY` (0→1, origin top) on the episode list container, or use a `MaxHeight` 0→large tween. Keep it to `AnimNormal`. Reduced-motion: the existing `Visibility` toggle already gives the static end-state; gate the Storyboard by binding to `AnimationsEnabled` (a `DataTrigger`) so under reduced motion it just snaps. If the accordion's internals make a clean height/scale animation hard (virtualization, M17 E2 note about the grid in an outer StackPanel/ScrollViewer), keep the animation to **opacity-only** (always safe) and document that height is instant. STOP and report only if even opacity can't attach.

- [ ] **Step 2: Build + commit** (`feat(motion): accordion expand/collapse animation`).

### Task D4: Shared-element card→hero morph (ATTEMPT; crossfade fallback)

**Files:** Create `src/VideoShelf.App/Motion/HeroTransition.cs`; modify `MainWindow.xaml` (an overlay layer), `VideoCard.xaml`/the creator grid, `SectionDetailView.xaml` (the hero banner).

> **Owner asked to attempt the true shared-element morph.** WPF has no built-in shared-element transition, and VideoShelf hosts views as persistent panels (no content-host swap), which makes a classic FLIP harder. The realistic approach: on card activation, capture the card thumbnail's screen rect, render a floating "ghost" copy in a top-level overlay `Canvas`, and animate its bounds (position+size) from the card rect to the destination hero banner's rect, fading the real hero in underneath; remove the ghost on completion. **Time-box this.** If after a genuine attempt it fights the persistent-view hosting / airspace / virtualization (the card may be virtualized-out, or the hero rect unknown until layout), FALL BACK to a crossfade + scale-from-card-center on the destination view (still tasteful) and **clearly document in the PR which path shipped**. Do NOT block the milestone on the full morph.

- [ ] **Step 1: Implement the ghost-overlay helper.** A static `HeroTransition.Play(FrameworkElement source, FrameworkElement targetPlaceholder, Panel overlayHost, Func<bool> shouldAnimate)` that: if `!shouldAnimate()` returns immediately (no-op, destination just shows); else captures `source` to a `VisualBrush`/`RenderTargetBitmap`, adds a `Rectangle` to `overlayHost` at the source's bounds (via `source.TransformToVisual(overlayHost)`), and animates its `Canvas.Left/Top/Width/Height` to the target's bounds over `AnimSlow`, removing it on `Completed`. Keep it self-contained; reduced-motion → skip.

- [ ] **Step 2: Add the overlay host** — a top-most `Canvas x:Name="HeroOverlay"` in `MainWindow.xaml` (sibling above views, below toasts), `IsHitTestVisible="False"`.

- [ ] **Step 3: Trigger on navigation** from a card to SectionDetail: in the card-activation path (the command that opens SectionDetail), after setting `CurrentView = SectionDetail`, call `HeroTransition.Play(thumbnailElement, heroBanner, HeroOverlay, () => motion.ShouldAnimate)`. Reaching the source thumbnail element + the destination hero rect from the VM is the hard part — this is view-level glue; do it in code-behind hooking the existing card-click, NOT in the VM. **If you cannot reliably obtain both rects, ship the crossfade fallback** (animate the destination SectionDetail view's opacity/scale on enter — which D1's `ViewTransition` already largely provides; in that case make the card→detail nav use a slightly stronger scale-from-center variant and document that the full shared-element morph was not feasible).

- [ ] **Step 4: Build + smoke.** Build clean; launch smoke. Manually note in the PR which path shipped (full morph vs crossfade fallback). Commit (`feat(motion): card->hero shared-element transition (or documented crossfade fallback)`).

### Task D5: Series-complete celebration

**Files:** Modify wherever the last episode of a series becomes watched (the watch-toggle / auto-watched path — likely `MainViewModel`/`PlayerViewModel` end-of-media, or the bulk mark-watched). Reuse the toast system + a light flourish.

- [ ] **Step 1:** When an action results in a series becoming fully watched (all episodes watched), show a celebratory toast (`ToastKind.Success`, e.g. "🎉 Finished <series>!") via `IToastService`. Detect "just completed" by checking, after a watched-toggle, whether the series' unwatched count went to zero (use the existing repo/VM data — do not add a Core query if one exists; if detecting completion requires a new Core method, STOP and report — prefer doing it from already-loaded VM state). Keep the "celebration" to the toast + optionally a brief confetti/scale flourish on the toast (reduced-motion: plain toast). Do NOT build a heavy particle system.

- [ ] **Step 2: Build + commit** (`feat(motion): series-complete celebration toast`).

### Task D6: PiP snap-to-corner + hover-fade

**Files:** Modify `src/VideoShelf.App/Views/MainWindow.xaml.cs` (PiP host animation) + the PiP host trigger in `MainWindow.xaml`.

The PiP host currently snaps instantly (`DataTrigger` sets `Width=360/Height=203`). Animate the shrink/grow. This is a *layout/host* animation (NOT over the `VideoView` surface), so it's safe — but it IS over-video airspace for capture, so verify-by-proxy.

- [ ] **Step 1:** Instead of (or in addition to) the instant `DataTrigger` setters, animate `Width`/`Height` (and `Margin` if used for corner placement) via `BeginAnimation` in code-behind when `IsPictureInPicture` changes — hook the `MainViewModel.PropertyChanged` for `IsPictureInPicture` in `MainWindow.xaml.cs`, and animate the `PlayerHost`'s `Width`/`Height` to the target over `AnimNormal` with `EaseInOut`. Reduced-motion: skip the animation, set the final size directly (keep the existing `DataTrigger` as the static fallback). **Caveat (M10/M19):** do not RenderTransform the HwndHost; animate layout `Width/Height`/`Margin` only.

- [ ] **Step 2: Hover-fade** — the PiP's own WPF chrome (close/expand buttons over the corner) can fade in on mouse-over and out on idle (a simple `Opacity` `Trigger` on `IsMouseOver`). This is WPF chrome, not the video surface — safe. Additive.

- [ ] **Step 3: Build + smoke.** Build clean. PiP motion is over-video → verify-by-proxy (smoke + the static PiP `--view PiP` end-state still captures the snapped size). Commit (`feat(motion): animate PiP snap-to-corner + chrome hover-fade`).

### Task D7: Now-playing in the window titlebar

**Files:** Modify `src/VideoShelf.App/Views/MainWindow.xaml` (the `ui:FluentWindow Title` + `ui:TitleBar Title`); expose the title on the VM.

- [ ] **Step 1: Test the title string** (pure).

```csharp
// tests/VideoShelf.App.Tests/Motion/NowPlayingTitleTests.cs
using Xunit; using Shouldly;
public class NowPlayingTitleTests
{
    [Theory]
    [InlineData("", "VideoShelf")]
    [InlineData("Big Buck Bunny", "Big Buck Bunny — VideoShelf")]
    public void WindowTitle_composes(string nowPlaying, string expected)
        => VideoShelf.App.ViewModels.MainViewModel.ComposeWindowTitle(nowPlaying).ShouldBe(expected);
}
```

- [ ] **Step 2: Implement** a pure `public static string ComposeWindowTitle(string nowPlaying) => string.IsNullOrEmpty(nowPlaying) ? "VideoShelf" : $"{nowPlaying} — VideoShelf";` on `MainViewModel`, plus an instance `WindowTitle` property that recomputes from the player title and `IsPlayerVisible` (raise its `PropertyChanged` when the player title changes / player opens/closes). Run the test, confirm pass.

- [ ] **Step 3: Bind** `ui:FluentWindow Title="{Binding WindowTitle}"` and the `ui:TitleBar Title="{Binding WindowTitle}"` in `MainWindow.xaml` (replace the hardcoded `"VideoShelf"`). Build clean.

- [ ] **Step 4: Commit** (`feat(motion): show now-playing title in the window titlebar`).

### Task D8: Group D finish PR #4

- [ ] **Step 1:** Full test gate green (adds ScrollMemory + NowPlayingTitle tests).
- [ ] **Step 2:** Real-app smoke. Sweep: Home/Browse (enter transition end-state — should look identical when settled), SectionDetail (accordion + hero end-state), PiP (snapped), and the titlebar (now-playing). Sweep subagent verdict + confirm no regression.
- [ ] **Step 3:** Whole-branch review; address blockers. **Note in the PR which hero path shipped (full morph vs crossfade fallback).**
- [ ] **Step 4:** Push, PR `M21 Group D — transitions, scroll memory, celebration, PiP, titlebar`, CI green, merge from main root, clean up. Proceed to Group E.

---

# GROUP E — Harness states + sweep + consolidation + ROADMAP flip (PR #5)

**Branch:** `feat/m21-harness-and-finish` (rebased on merged main). **Outcome:** harness motion/toast states added to the sweep, full sweep + smoke, milestone consolidated, ROADMAP flipped.

### Task E1: Harness states + sweep coverage

**Files:** Modify `src/VideoShelf.App/Harness/HarnessRunner.cs` (+ `HarnessOptions.cs` if a new flag is needed), `tools/harness/Run-VisualSweep.ps1`.

- [ ] **Step 1:** Ensure these capture states exist as `--view` cases (some added in B4/C3): a toast shown, a list skeleton (`IsLoading`), the PiP snapped, the titlebar now-playing (covered by `--play`/`Player`). Add any missing via the `_postSettleAction` pattern. Reduced-motion states are not separately capturable (static) — skip.
- [ ] **Step 2:** Add the new `--view` names to `Run-VisualSweep.ps1`'s view list so the standing sweep covers them.
- [ ] **Step 3: Commit** (`chore(motion): harness states + sweep coverage for toasts/skeletons/PiP`).

### Task E2: Consolidation, final review, ROADMAP flip, finish PR #5

- [ ] **Step 1: Full test gate** green: `dotnet test VideoShelf.slnx -c Release --nologo -v q`. Record the new total (≈ 915 + MotionPolicy 4 + Toast 4 + bulk-inverse + ScrollMemory 2 + NowPlayingTitle 2).
- [ ] **Step 2: App-launch smoke** on the real, populated library with `--done-signal` (the M17/M18/M20 lesson) — confirm a clean launch with all the new overlays/resources. Dispatch a Sonnet subagent to read the new static end-state PNGs + return a combined verdict.
- [ ] **Step 3: Whole-branch review** across Group E; address blockers.
- [ ] **Step 4: Flip the ROADMAP row** (final PR — rides this branch per the owner rule). In `C:\Agent Projects\VideoShelf\ROADMAP.md`, change the M21 row Status `[ ] Not started` → `✅ Merged`, set Plan to this file's link, set PR to the five PR numbers, and write a one-line shipped summary. Append a **decision-log entry** capturing: the locked/cut scope; the reduced-motion gate (`MotionPolicy`/`SystemParameters.ClientAreaAnimation`); which hero path shipped (full morph vs crossfade fallback); the "motion is invisible to the static sweep → verify by unit tests + end-states + smoke + manual gate" insight; the persistent-views-never-destroyed constraint (enter transitions via `IsVisibleChanged`); and the M8→M21 no-runner streak. Note `[[wpfui-theming-and-visual-verification]]` and that NO screen-reader semantics were reintroduced (PR #77).
- [ ] **Step 5: Finish** — push, PR `M21 Group E — harness/sweep + ROADMAP flip`, CI green, merge `--merge --delete-branch` from the main repo root, clean up the worktree, pull main.
- [ ] **Step 6: Ping the owner** (Phase-B handoff) per the roadmap skill: M21 merged & CI-green; v4 (M16–M21) complete; ask what's next (re-scope / new version).

---

## Self-review (author checklist — done)

- **Spec coverage:** reduced-motion gate → A1; animation tokens → A2; thumbnail fade-in → A3; card hover → A4; toasts+undo (favorite/watchlist/bulk/rename/remove-source) → B1–B3; resume toast → B3; skeletons → C1; live scan count → C2; page transitions → D1; scroll memory → D2; accordion → D3; shared-element (attempt+fallback) → D4; series-complete → D5; PiP snap+hover → D6; now-playing titlebar → D7; harness/sweep → B4/C3/E1. Cut items (cheat-sheet, splash/About, first-run tour, what's-new) — explicitly excluded. Screen-reader re-introduction — explicitly forbidden (STOP guard).
- **Placeholder scan:** pure pieces (MotionPolicy, ToastService, ScrollOffsetStore, ComposeWindowTitle) are fully written with tests; animation glue is concrete with the first instance shown + the repeat-pattern named; the two genuinely view-dependent/risky pieces (hero morph D4, accordion height D3) carry explicit attempt-then-documented-fallback instructions, not placeholders.
- **Type consistency:** `IMotionPolicy.ShouldAnimate`, `IToastService.Show/Dismiss/Toasts`, `ToastViewModel.Message/Kind/UndoCommand/HasUndo`, `ScrollOffsetStore.Save/TryGet`, `MainViewModel.ComposeWindowTitle/WindowTitle/AnimationsEnabled/Toasts`, `ViewTransition.Enabled/ShouldAnimate` used consistently across tasks.
- **Known unknowns flagged as STOP-and-report:** the DI access point for `ViewTransition.ShouldAnimate`; reaching `IToastService` from deep VM chains; the bulk inverse extraction; whether scan exposes a real incremental count (no fake); obtaining both rects for the hero morph (else fallback); accordion height animation feasibility; series-completion detection without a new Core query; PiP layout-only animation (never RenderTransform the HwndHost).
- **No Core/schema change** anywhere — App-layer only; M8→M21 no-`user_version`-runner streak preserved (asserted in E2). No `AutomationProperties`/screen-reader semantics reintroduced (PR #77 held).
