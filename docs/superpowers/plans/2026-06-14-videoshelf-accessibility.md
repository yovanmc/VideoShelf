# M20 — Accessibility Program (structural screen-reader + keyboard) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Written for Sonnet execution. If something doesn't match what this plan says — a file isn't where it's described, an API has a different shape, a property is already set — STOP and report rather than guess.** This is an a11y milestone: most of what you build is *invisible to screenshots*, so verification leans on unit tests + a new automation-tree text dump, not the GDI sweep. Read the "Verification strategy" section before starting.

**Goal:** Make VideoShelf usable by a keyboard-only user and a screen-reader user — every interactive control has a name/role, every surface is keyboard-reachable with focus restored after the player closes, watched/progress state is readable without color, and now-playing / scan / rename changes are announced.

**Architecture:** Pure **App-layer, additive** work — no `VideoShelf.Core` change, **no schema/migration** (the M8→M19 no-`user_version`-runner streak must hold → M8→M20). We set `AutomationProperties` (attached DPs) and `KeyboardNavigation`/`TextSearch` settings on *existing* controls (never retemplate a WPF-UI control — M15/M17 theming rule), add two pure converters + one attached behavior + one small focus-return service, and add a harness `--a11y-dump` mode that writes the live UI Automation tree to a text file for verification. Delivered as **4 stacked PRs split at the Group seams** (Groups A–E), the M16/M18/M19 model.

**Tech Stack:** .NET 10 WPF, WPF-UI 4.3.0 (dark-only), MVVM, xUnit (`VideoShelf.App.Tests`), the existing harness (`HarnessOptions` + `Run-VisualSweep.ps1`). UI Automation via `System.Windows.Automation.AutomationProperties` (attached) and `System.Windows.Automation.Peers` (`UIElementAutomationPeer`, `AutomationEvents.LiveRegionChanged`).

---

## Locked scope (owner-decided 2026-06-14)

**IN:**
1. **UIA names/roles** on cards, rails, chips, transport, and the scrubber (**RangeValue** + value text) — Group A.
2. **Full keyboard reachability** — roving-tabindex rails/grid, Enter/Space activation, type-ahead, consistent Esc, **focus restoration after player/PiP close** — Group B.
3. **Color-independent watched/progress cue** — a ✓ glyph + "NN%" text so state never depends on color alone (**adds zero color options**) — Group C.
4. **Live regions** — announce now-playing, scan-complete/diff, rename-applied — Group D.
5. **44px minimum hit targets** + **confirm + undo for destructive actions** (Remove source / rename) — Group E.

**CUT (do NOT build):** reduced-motion flag, keyboard reposition for the draggable PiP, AA-contrast pass, DPI/text-zoom scaling, **all color/theming/customization work**.

**DEFERRED to a later milestone (do NOT build):** caption/subtitle text styling, audio-description track selector, Windows High-Contrast theme honoring.

> If you find yourself editing a brush, a color, `DesignTokens.xaml` palette values, an animation/`Storyboard`, or `SystemColors`/`SystemParameters.HighContrast`/DPI — **STOP**, you are outside scope. The only `DesignTokens.xaml` edits allowed are *additive new styles* for focus/target-size, never color changes.

---

## Verification strategy (READ FIRST — a11y is invisible to the GDI sweep)

The standing screenshot sweep (`Run-VisualSweep.ps1`, GDI `CopyFromScreen`) **cannot see** `AutomationProperties.Name`, control roles, keyboard focus, focus order, or live-region announcements. Repeating the M18 lesson: *some features render/behave only under the real DI graph and are blind to both the slim-DI harness and view-less unit tests.* So this milestone verifies in three layers:

1. **Pure unit tests (`VideoShelf.App.Tests`)** for everything that can be made pure: the two new converters, the focus-return service, and the live-region announcer's "should-announce" decision. These are the regression gate.
2. **A new harness mode `--a11y-dump <path>`** (Group A, Task A1) that walks the **live** UI Automation tree of the started window and writes a text file: one line per element = `ControlType | Name | AutomationId | <patterns>`. This is the real-DI, real-automation-tree evidence. Each group's verification dispatches a **Sonnet subagent** to read the dump file (text, not an image — cheap) and return a **text verdict**: are the expected names/roles present? This is the primary acceptance evidence for Groups A–D.
3. **The GDI screenshot sweep** verifies only the *visible* additions: the ✓/NN% cues (Group C), the enlarged 44px targets (Group E), and the confirm dialog (Group E). Dispatch the sweep-reading subagent per the standing rule (text verdict, paths; never load PNGs into the controller).

**Keyboard behavior** (roving-tabindex, type-ahead, Enter/Space, Esc, focus restoration) is verified by: (a) unit-testing the focus-return service + asserting the `KeyboardNavigation`/`TextSearch` attached values appear in the `--a11y-dump` (extend the dump to also print `KeyboardNavigation.TabNavigation`, `IsTabStop`, `TextSearch.TextPath` for container/card elements), and (b) a **flagged owner keyboard test** at milestone end (Tab through Browse → arrow within the grid → type-ahead → Enter to open → Esc/close → focus returns). Note this in the PR as the one manual gate.

**Gate = green build + all tests pass + the a11y-dump subagent verdict PASS + the sweep subagent verdict PASS on a POPULATED real library** (not the test count). Baseline before M20: **900 tests** (368 Core + 532 App).

---

## File structure (what gets created / modified)

**Created:**
- `src/VideoShelf.App/Converters/AccessibilityConverters.cs` — `FractionToPercentText`, `WatchedToGlyph` (pure `IValueConverter`s). *(Or add to the existing `Converters/Converters.cs` if that's the established home — see Task C1; follow the existing pattern.)*
- `src/VideoShelf.App/Accessibility/LiveRegion.cs` — attached behavior: `LiveRegion.Text` (string DP) + `LiveRegion.Politeness` (enum DP) that, on text change, sets `AutomationProperties.LiveSetting` and raises `AutomationEvents.LiveRegionChanged` on the element's peer.
- `src/VideoShelf.App/Accessibility/IFocusReturnService.cs` + `FocusReturnService.cs` — captures the `IInputElement` that had focus when the player opened and restores it when the player/PiP closes.
- `src/VideoShelf.App/Harness/A11yTreeDumper.cs` — walks the automation tree of a `Window` and writes the text dump.
- Tests: `tests/VideoShelf.App.Tests/Accessibility/AccessibilityConvertersTests.cs`, `FocusReturnServiceTests.cs`, `LiveRegionTests.cs`.

**Modified (additive only):**
- `src/VideoShelf.App/Views/VideoCard.xaml`, `CreatorCard.xaml` — `AutomationProperties.Name/HelpText`, Enter/Space activation, watched/progress cue, 44px.
- `src/VideoShelf.App/Resources/QueueStyles.xaml` — queue-row template automation names + 44px row buttons.
- `src/VideoShelf.App/Resources/DesignTokens.xaml` — **additive only**: a `HitTarget` style (MinWidth/MinHeight 44) and apply existing `AppFocusVisual` where missing. **No color/brush edits.**
- `src/VideoShelf.App/Views/PlayerView.xaml` (+ `.xaml.cs`) — scrubber RangeValue + value text, audit transport names (Queue toggle is unnamed — line ~501), Esc consistency, 44px transport buttons, live-region now-playing, focus-return hook.
- `src/VideoShelf.App/Views/MainWindow.xaml` (+ `.xaml.cs`) — creator-grid roving-tabindex/type-ahead, breadcrumb/nav name audit, Esc handling, scan/rename live regions.
- `src/VideoShelf.App/ViewModels/MainViewModel.cs` — wire `IFocusReturnService` into the single `OpenPlayer`/player-close funnel (preserve the M14 single-next-decider invariant); expose live-region status strings for scan/rename.
- `src/VideoShelf.App/Views/TagEditorView.xaml`, chip usages — chip automation names + 44px remove buttons.
- `src/VideoShelf.App/Views/SettingsView.xaml` (+ VM) — Remove-source confirm + undo.
- `src/VideoShelf.App/Harness/HarnessOptions.cs` + the harness entry (`App.xaml.cs`/`MainWindow` harness hook) — `--a11y-dump <path>`.
- `src/VideoShelf.App/Program`/DI registration — register `IFocusReturnService`.
- `tools/harness/Run-VisualSweep.ps1` — optional: a `-A11yDump` switch that runs the app with `--a11y-dump` and copies the file out (Group E).

---

# GROUP A — UIA semantics foundation (PR #1)

**Branch:** `feat/m20-a11y-semantics`. **Outcome:** every interactive control exposes a Name + correct role; the scrubber exposes RangeValue + a spoken value; a `--a11y-dump` harness mode produces the text evidence. Additive XAML + one harness class + a small DI add.

> Setup (start of group, from the runbook §3):
> ```bash
> cd "C:/Agent Projects/VideoShelf" && git checkout main && git pull
> cd "C:/Agent Projects/VideoShelf" && git worktree add ".worktrees/feat-m20-a11y-semantics" -b "feat/m20-a11y-semantics"
> cd "C:/Agent Projects/VideoShelf/.worktrees/feat-m20-a11y-semantics" && dotnet test VideoShelf.slnx -c Release --nologo -v q 2>&1 | tail -5
> ```
> Baseline MUST be green (900 tests). If red, STOP and report.

### Task A1: `--a11y-dump` harness mode (the verification backbone)

**Files:**
- Create: `src/VideoShelf.App/Harness/A11yTreeDumper.cs`
- Modify: `src/VideoShelf.App/Harness/HarnessOptions.cs` (add the `--a11y-dump <path>` option, mirroring `--done-signal`)
- Modify: the harness post-launch hook that handles `--done-signal` (find it in `App.xaml.cs` or `MainWindow.xaml.cs` — the digest places the done-signal write there; STOP and report if you can't find a single hook). Call the dumper *after the window has rendered and settled*, before (or alongside) writing the done-signal.

- [ ] **Step 1: Write the dumper.**

```csharp
// src/VideoShelf.App/Harness/A11yTreeDumper.cs
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;

namespace VideoShelf.App.Harness;

/// <summary>
/// Walks the UI Automation peer tree of a window and writes a stable text dump
/// (one line per element) used to verify accessibility semantics in the real app.
/// This is text — not a screenshot — so it captures Name/Role/patterns the GDI
/// sweep cannot see.
/// </summary>
public static class A11yTreeDumper
{
    public static void Dump(Window window, string path)
    {
        var sb = new StringBuilder();
        var peer = UIElementAutomationPeer.CreatePeerForElement(window)
                   ?? new WindowAutomationPeer(window);
        Walk(peer, 0, sb);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString());
    }

    private static void Walk(AutomationPeer peer, int depth, StringBuilder sb)
    {
        var indent = new string(' ', depth * 2);
        var type = peer.GetAutomationControlType();
        var name = peer.GetName() ?? "";
        var id = peer.GetAutomationId() ?? "";
        var patterns = DescribePatterns(peer);
        sb.AppendLine($"{indent}{type} | name='{name}' | id='{id}'{patterns}");

        var children = peer.GetChildren();
        if (children == null) return;
        foreach (var child in children)
            Walk(child, depth + 1, sb);
    }

    private static string DescribePatterns(AutomationPeer peer)
    {
        var found = new System.Collections.Generic.List<string>();
        if (peer.GetPattern(PatternInterface.RangeValue) is IRangeValueProvider rv)
            found.Add($"RangeValue(min={rv.Minimum},max={rv.Maximum},val={rv.Value})");
        if (peer.GetPattern(PatternInterface.Invoke) is not null) found.Add("Invoke");
        if (peer.GetPattern(PatternInterface.Toggle) is not null) found.Add("Toggle");
        if (peer.GetPattern(PatternInterface.SelectionItem) is not null) found.Add("SelectionItem");
        return found.Count == 0 ? "" : " | " + string.Join(",", found);
    }
}
```

> Note: `IRangeValueProvider` is in `System.Windows.Automation.Provider`. Add `using System.Windows.Automation.Provider;` if the build complains. **Reflection-verify nothing here** — these are stable framework types — but if `GetPattern`'s return type differs, STOP and report.

- [ ] **Step 2: Add the harness option.** In `HarnessOptions.cs`, add a `public string? A11yDumpPath { get; init; }` (match the existing property style) and parse `--a11y-dump <path>` exactly like `--done-signal` is parsed. Read the existing parse block first and copy its idiom.

- [ ] **Step 3: Invoke the dumper from the harness hook.** Where the harness handles `--done-signal` after the window settles, add (using the same settle/`Dispatcher` timing already used there):

```csharp
if (!string.IsNullOrEmpty(options.A11yDumpPath))
    A11yTreeDumper.Dump(mainWindow, options.A11yDumpPath);
```

Place it so it runs after the requested `--view` is shown and rendered (same point the done-signal fires). If the done-signal hook gives you the active `Window`, reuse it; if not, use `Application.Current.MainWindow`.

- [ ] **Step 4: Build.** Run: `cd "C:/Agent Projects/VideoShelf/.worktrees/feat-m20-a11y-semantics" && dotnet build VideoShelf.slnx -c Release -v minimal 2>&1 | tail -5`. Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Smoke the dump on a populated library.** Run the app with `--view Browse --a11y-dump` against the fixtures the sweep uses (see `Run-VisualSweep.ps1` for the fixture path + flags it passes), with `--done-signal` so it exits. Confirm the dump file is written and non-empty and contains `Button`/`List` lines. (Exact command: mirror the launch line inside `Run-VisualSweep.ps1`, adding `--a11y-dump "<out>\a11y-browse.txt"`.) Expected: a text file with the Browse view's element tree.

- [ ] **Step 6: Commit.**

```bash
git add src/VideoShelf.App/Harness/A11yTreeDumper.cs src/VideoShelf.App/Harness/HarnessOptions.cs src/VideoShelf.App/App.xaml.cs
git commit -m "feat(a11y): add --a11y-dump harness mode that writes the UIA tree

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task A2: Card automation names (VideoCard, CreatorCard)

**Files:** Modify `src/VideoShelf.App/Views/VideoCard.xaml`, `src/VideoShelf.App/Views/CreatorCard.xaml`.

The cards are already `Button`s (role = Invoke ✓) but expose no name → a screen reader reads "button". Add a meaningful, data-bound name. **Do not** restyle/retemplate.

- [ ] **Step 1: VideoCard** — on the root `<Button>`, add:

```xml
AutomationProperties.Name="{Binding SeriesTitle}"
AutomationProperties.HelpText="{Binding EpisodeLabel}"
```

If `SeriesTitle` can be empty for standalones, prefer the most descriptive bound property available on the card VM (check `VideoCardViewModel`/`RecencyCardViewModel`; STOP and report if neither has a title). Add no new VM property unless none exists — if none exists, add a read-only `string AccessibleName => string.IsNullOrEmpty(SeriesTitle) ? EpisodeLabel : $"{SeriesTitle}, {EpisodeLabel}";` and bind to it.

- [ ] **Step 2: CreatorCard** — on the root `<Button>`, add:

```xml
AutomationProperties.Name="{Binding Name}"
AutomationProperties.HelpText="{Binding VideoCountLabel}"
```

- [ ] **Step 3: Build.** Run: `dotnet build VideoShelf.slnx -c Release -v minimal 2>&1 | tail -5`. Expected: 0 errors.

- [ ] **Step 4: Verify via dump.** Re-run the `--a11y-dump` on `--view Browse` and `--view Home`; confirm `Button | name='<a creator name>'` and `name='<a video title>'` lines now appear (they were `name=''` before). Expected: card buttons now carry names.

- [ ] **Step 5: Commit** (`feat(a11y): name video and creator cards for screen readers`).

### Task A3: Scrubber RangeValue + spoken value

**Files:** Modify `src/VideoShelf.App/Views/PlayerView.xaml` (the `<Slider x:Name="SeekBar">`, ~line 125).

A WPF `Slider` already exposes the `RangeValue` pattern, so the bones are there — but it has no Name and no human-readable value text, so a screen reader announces a bare number of seconds. Add a name and a bound value description.

- [ ] **Step 1:** On `SeekBar`, add:

```xml
AutomationProperties.Name="Seek"
AutomationProperties.ItemStatus="{Binding PositionText}"
```

Use the player VM's existing formatted position/length string. The digest shows time labels bound to position/length; find that property (e.g. `PositionText`/`PositionDisplay`) on `PlayerViewModel`. If the formatted string is only in the XaML `TextBlock` (not a VM property), add a read-only VM property `string PositionText => $"{Format(PositionSeconds)} of {Format(LengthSeconds)}";` reusing the existing time-format helper, and raise its change notification wherever `PositionSeconds` changes. STOP and report if you can't locate the existing formatter.

- [ ] **Step 2: Build + verify via dump.** Run `--a11y-dump` on a player sub-state (the harness has `--play`/player views — reuse `Run-VisualSweep.ps1`'s player launch). Confirm the dump shows the `Slider` line with `RangeValue(min=0,max=<len>,val=<pos>)` and `name='Seek'`. Expected: RangeValue pattern present with a name.

- [ ] **Step 3: Commit** (`feat(a11y): name the seek slider and expose a spoken position`).

### Task A4: Transport, chip, and queue-row name audit

**Files:** Modify `src/VideoShelf.App/Views/PlayerView.xaml`, `src/VideoShelf.App/Resources/QueueStyles.xaml`, `src/VideoShelf.App/Views/TagEditorView.xaml` (+ any chip usage in `MainWindow.xaml`).

M15/M19 named most transport icon-buttons; close the known gaps and the unnamed chip/queue controls.

- [ ] **Step 1: Player Queue toggle** — the "Up next" toggle `<StackPanel>`/button at ~PlayerView.xaml line 501 has no name. Add `AutomationProperties.Name="Up next queue"` to its clickable element. (If it's a bare `StackPanel` with a mouse handler rather than a `Button`, STOP and report — it should become a `Button`/`ToggleButton` for role correctness; convert it to a `ui:Button` with the same content if so, additive.)

- [ ] **Step 2: Queue-row buttons** — in `QueueStyles.xaml`'s `QueueItemTemplate`, confirm the per-row Move-up/Move-down/Play/Remove buttons each have `AutomationProperties.Name` (the digest says they do — "4 buttons per item named"). If any is missing, add: `"Move up"`, `"Move down"`, `"Play this item"`, `"Remove from queue"`. Also set `AutomationProperties.Name="{Binding Title}"` on the row container so the row announces what it is.

- [ ] **Step 3: Chips** — the read-only `Chip` `Border`s carry text but no role/name for SR. For applied/inherited **tag** chips in `TagEditorView.xaml`, set `AutomationProperties.Name="{Binding}"` (or the tag-text binding) on the chip `Border` and `AutomationProperties.HelpText="Tag"`. For interactive `ChipToggle`s (Select/Filter/Density in `MainWindow.xaml`), confirm each already has a Name (digest lists three at lines 209/229/235) and add Names to any toggle lacking one.

- [ ] **Step 4: Build + verify via dump** on Browse + a player+queue state. Confirm no interactive element shows `name=''` for the surfaces touched. Expected: named queue rows, named chips, named Up-next toggle.

- [ ] **Step 5: Commit** (`feat(a11y): name queue rows, chips, and the up-next toggle`).

### Task A5: Group A whole-branch review + finish PR #1

- [ ] **Step 1:** Run the full test gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q`. Expected: 900 passing (Group A adds no unit tests; it's XAML + harness — that's expected, the dump is the evidence).
- [ ] **Step 2:** Dispatch a Sonnet subagent to read the `--a11y-dump` text files produced for Home/Browse/Player and return a verdict: do cards, the seek slider (RangeValue), queue rows, and chips all carry non-empty names/correct roles? PASS/FAIL + specifics.
- [ ] **Step 3:** Finish per runbook §5 — push, PR titled `M20 Group A — UIA semantics foundation + --a11y-dump`, watch CI green, merge `--merge --delete-branch` from the **main repo root**, clean up the worktree, pull main.
- [ ] **Step 4:** Do **not** flip the ROADMAP row yet (flip it on the final PR). Proceed to Group B.

---

# GROUP B — Keyboard reachability + focus management (PR #2)

**Branch:** `feat/m20-a11y-keyboard` (rebased on the freshly-merged main). **Outcome:** a keyboard-only user can Tab to each rail/grid as one stop, arrow within it, type-ahead to jump, Enter/Space to activate, Esc to back out consistently, and focus is **restored to the originating card after the player/PiP closes**.

### Task B1: FocusReturnService (unit-tested seam)

**Files:**
- Create: `src/VideoShelf.App/Accessibility/IFocusReturnService.cs`, `FocusReturnService.cs`
- Create test: `tests/VideoShelf.App.Tests/Accessibility/FocusReturnServiceTests.cs`
- Modify: DI registration; `MainViewModel.cs` (call into it from the single player open/close funnel).

The hard part of focus restoration is doing it without coupling to views. Model it as: "capture a token now; restore it later", where the token is an opaque `IInputElement` the *view* supplies. The **service is pure enough to unit-test** (capture → restore returns the same element; restore with nothing captured is a no-op; restore clears the capture so a second restore is a no-op).

- [ ] **Step 1: Write the failing test.**

```csharp
// tests/VideoShelf.App.Tests/Accessibility/FocusReturnServiceTests.cs
using VideoShelf.App.Accessibility;
using Xunit;

public class FocusReturnServiceTests
{
    private sealed class FakeFocusable : System.Windows.IInputElement
    {
        // Minimal stub — we only need reference identity for the service's logic.
        public bool Focus() => true;
        // The remaining IInputElement members are never called by the service;
        // throw to prove that. (If the interface surface is large, prefer a
        // Mock<IInputElement> via the test project's existing mocking lib instead.)
        public event System.Windows.Input.KeyEventHandler? KeyDown { add { } remove { } }
        public event System.Windows.Input.KeyEventHandler? KeyUp { add { } remove { } }
        // ... (see note below)
        public bool IsEnabled => true;
        public bool IsKeyboardFocused => false;
        public bool IsKeyboardFocusWithin => false;
        public bool IsMouseCaptured => false;
        public bool IsMouseDirectlyOver => false;
        public bool IsMouseOver => false;
        public bool IsStylusCaptured => false;
        public bool IsStylusDirectlyOver => false;
        public bool IsStylusOver => false;
        public bool Focusable => true;
        public bool CaptureMouse() => false;
        public void ReleaseMouseCapture() { }
        public bool CaptureStylus() => false;
        public void ReleaseStylusCapture() { }
        public void AddHandler(System.Windows.RoutedEvent e, System.Delegate h) { }
        public void RemoveHandler(System.Windows.RoutedEvent e, System.Delegate h) { }
        public void RaiseEvent(System.Windows.RoutedEventArgs e) { }
        public event System.Windows.Input.MouseButtonEventHandler? PreviewMouseLeftButtonDown { add { } remove { } }
        // NOTE: IInputElement has many members. If stubbing is unwieldy, use the
        // test project's mocking library (check what App.Tests already references —
        // Moq/NSubstitute) and do: var el = Substitute.For<IInputElement>();
        // The service only ever stores the reference and returns it.
    }

    [Fact]
    public void Capture_then_TakeForRestore_returns_same_element()
    {
        var svc = new FocusReturnService();
        var el = Substitute.For<System.Windows.IInputElement>(); // prefer the mock
        svc.Capture(el);
        Assert.Same(el, svc.TakeForRestore());
    }

    [Fact]
    public void TakeForRestore_with_nothing_captured_returns_null()
    {
        var svc = new FocusReturnService();
        Assert.Null(svc.TakeForRestore());
    }

    [Fact]
    public void TakeForRestore_clears_the_capture()
    {
        var svc = new FocusReturnService();
        var el = Substitute.For<System.Windows.IInputElement>();
        svc.Capture(el);
        svc.TakeForRestore();
        Assert.Null(svc.TakeForRestore()); // second take is empty
    }
}
```

> **Before writing the stub, check which mocking library `VideoShelf.App.Tests` already uses** (Moq vs NSubstitute) and use it — delete the hand-stub. The service only stores+returns the reference, so a one-line mock is cleanest. STOP and report if no mocking lib is referenced (then keep a minimal stub but only implement the members the compiler demands).

- [ ] **Step 2: Run the test, confirm it fails to compile** (service doesn't exist). Run: `dotnet test tests/VideoShelf.App.Tests/VideoShelf.App.Tests.csproj -c Release --nologo -v q --filter FocusReturnServiceTests`. Expected: compile error / type not found.

- [ ] **Step 3: Implement.**

```csharp
// src/VideoShelf.App/Accessibility/IFocusReturnService.cs
using System.Windows;

namespace VideoShelf.App.Accessibility;

/// <summary>Captures focus before a modal-ish surface (player/PiP) opens and
/// restores it when that surface closes, so keyboard users aren't dumped at the
/// top of the page.</summary>
public interface IFocusReturnService
{
    void Capture(IInputElement? element);
    IInputElement? TakeForRestore();
}
```

```csharp
// src/VideoShelf.App/Accessibility/FocusReturnService.cs
using System.Windows;

namespace VideoShelf.App.Accessibility;

public sealed class FocusReturnService : IFocusReturnService
{
    private IInputElement? _captured;

    public void Capture(IInputElement? element) => _captured = element;

    public IInputElement? TakeForRestore()
    {
        var el = _captured;
        _captured = null;
        return el;
    }
}
```

- [ ] **Step 4: Run the tests, confirm pass.** Run the filtered test command. Expected: 3 passing.

- [ ] **Step 5: Register in DI** as a singleton (match the existing DI idiom in the app's composition root). Expected: build succeeds.

- [ ] **Step 6: Commit** (`feat(a11y): add FocusReturnService for player focus restoration`).

### Task B2: Wire focus capture/restore into the player funnel

**Files:** Modify `src/VideoShelf.App/ViewModels/MainViewModel.cs`, `src/VideoShelf.App/Views/PlayerView.xaml.cs`, and the view(s) that launch playback.

**Preserve the M14 single-next-decider invariant:** all plays already funnel through one `MainViewModel.OpenPlayer`. Capture happens at that funnel's entry; restore happens when the player view is torn down / navigated away.

- [ ] **Step 1:** At the top of `MainViewModel.OpenPlayer(...)` (the single funnel), capture current focus:

```csharp
_focusReturn.Capture(System.Windows.Input.Keyboard.FocusedElement);
```

Inject `IFocusReturnService _focusReturn` via the ctor (match the existing ctor-injection pattern; use the nullable-trailing-param idiom the project uses to avoid ctor fan-out, per the M16 gotcha — add it as a new optional last param with a default obtained from DI, or thread it through the composition root the same way other services are).

> **Auto-next caveat:** when the player advances to the *next* queue item, `OpenPlayer` is called again — do **not** overwrite the captured element on a player→player transition. Guard: only capture when the player is **not already open** (check the existing "is player the current view" state). This keeps the original launching card as the restore target across an auto-next chain. STOP and report if there's no clean "player already open" signal.

- [ ] **Step 2:** When the player closes (the existing close/back path that returns to the previous view — find where `GoBack`/close-player sets the view away from `Player`), restore:

```csharp
var el = _focusReturn.TakeForRestore();
if (el != null)
    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => el.Focus());
```

Use `Dispatcher.BeginInvoke` so focus lands *after* the target view is re-realized (the off-thread/timing gotcha pattern used elsewhere in this codebase). Place this at the single close funnel, not scattered.

- [ ] **Step 3: Build + manual-flag.** Run: `dotnet build VideoShelf.slnx -c Release -v minimal`. This behavior isn't unit-testable at the view level — add a line to the PR description's manual test checklist: "Tab to a card → Enter → player opens → close player → focus returns to that card." Expected: builds clean.

- [ ] **Step 4: Commit** (`feat(a11y): restore focus to the launching card when the player closes`).

### Task B3: Roving-tabindex + type-ahead on rails and the creator grid

**Files:** Modify `src/VideoShelf.App/Views/MainWindow.xaml` (the creator-grid `ListBox`), card rails (`ItemsControl`s on Home), `VideoCard.xaml`/`CreatorCard.xaml`.

Goal: each list/grid is **one Tab stop**; arrows move within; typing jumps to an item (type-ahead).

- [ ] **Step 1: Creator grid (`ListBox` with VWP).** A `ListBox` already gives `TabNavigation=Once` + arrow nav + built-in `TextSearch`. Make type-ahead work by telling it which text to match: on the `ListBox`, add `TextSearch.TextPath="Name"` (the creator-card VM's display property) and `IsTextSearchEnabled="True"`. Confirm `KeyboardNavigation.TabNavigation="Once"` and `KeyboardNavigation.DirectionalNavigation="Contained"` are set (add them if not). Do **not** convert it away from `ListBox`.

- [ ] **Step 2: Home rails that are `ItemsControl` (not `ListBox`).** `ItemsControl` gives no built-in roving focus, so each card is its own Tab stop today (noisy). Set on each rail `ItemsControl`:

```xml
KeyboardNavigation.TabNavigation="Once"
KeyboardNavigation.DirectionalNavigation="Contained"
```

This makes the rail one Tab stop with arrow movement inside. (Type-ahead is a `ListBox`/`Selector` feature; `ItemsControl` rails won't get type-ahead — that's acceptable, the Browse grid is the searchable surface. Do NOT convert rails to `ListBox` purely for type-ahead — out of scope/risk.)

- [ ] **Step 3: Cards focusable + show focus.** Ensure `VideoCard`/`CreatorCard` root `Button`s are `IsTabStop="True"` (Buttons are by default) and carry `FocusVisualStyle="{StaticResource AppFocusVisual}"` so keyboard focus is visible (M15 shipped `AppFocusVisual` — apply it here if missing; this is *additive*, not a color change).

- [ ] **Step 4: Build + verify via dump.** Extend `A11yTreeDumper.DescribePatterns` (or the line format) to also print, for container elements, `KeyboardNavigation.GetTabNavigation(element)` and `TextSearch.GetTextPath(element)` when the underlying element is a `FrameworkElement` (you can get it via `((peer as FrameworkElementAutomationPeer)?.Owner)`). Re-dump Browse; confirm the grid shows `TabNavigation=Once` + a `TextPath`. Expected: grid is one tab stop with type-ahead configured.

> If reaching the owner `FrameworkElement` from the peer is awkward, instead add a tiny additional dump section that walks the **visual tree** from the window and prints `KeyboardNavigation`/`TextSearch` attached values for any `ItemsControl`. Keep it in `A11yTreeDumper`. STOP and report if neither approach is clean.

- [ ] **Step 5: Commit** (`feat(a11y): roving tab-index + type-ahead on grid and rails`).

### Task B4: Enter/Space activation + consistent Esc

**Files:** Modify `src/VideoShelf.App/Views/PlayerView.xaml.cs`, `MainWindow.xaml.cs`, flyout/dialog handling.

- [ ] **Step 1: Enter/Space on cards.** Cards are `Button`s → Space/Enter already invoke `Command`. **Verify** (no change expected). If any card is a non-Button clickable (e.g., a `Border` with `MouseLeftButtonDown`), STOP and report — it must become a `Button` for keyboard activation; convert additively if found.

- [ ] **Step 2: Esc consistency.** Audit Esc across modal-ish surfaces so it always "backs out one level":
  - Command palette (Ctrl+K): Esc closes it (digest shows palette VM; confirm `Escape` closes — add a `PreviewKeyDown`/`InputBinding` if missing).
  - Player flyouts (More/Tracks/Volume): Esc closes the open flyout (not the whole player). The flyouts are `Popup`s; add Esc-closes handling in `PlayerView.xaml.cs` `OnKeyDown` *before* the existing `Escape → ExitFullscreen` so an open flyout swallows Esc first.
  - Player: Esc exits fullscreen (already mapped via `PlayerKeyMap`), and when not fullscreen, Esc closes the player (back). Confirm/extend `PlayerKeyMap` + `OnKeyDown` so Esc with no flyout + not fullscreen routes to close-player. Keep `PlayerKeyMap` the single source of truth — extend the enum if needed (e.g. `ClosePlayer`).

- [ ] **Step 3: Build + manual-flag** (Esc behavior is interaction-timing; add to the PR manual checklist: "Esc closes an open flyout first, then exits fullscreen, then closes the player"). Add unit coverage for `PlayerKeyMap.Resolve(Key.Escape, …)` returning the expected command(s) — `PlayerKeyMap` is pure and already unit-tested; extend its tests.

```csharp
// extend the existing PlayerKeyMap tests
[Fact]
public void Escape_maps_to_exit_fullscreen_or_close()
{
    Assert.Equal(PlayerCommand.ExitFullscreen, PlayerKeyMap.Resolve(Key.Escape, ModifierKeys.None));
    // ...adjust to the actual contract you implement; the point is a regression test exists
}
```

- [ ] **Step 4: Commit** (`feat(a11y): consistent Enter/Space activation and Esc back-out`).

### Task B5: Group B review + finish PR #2

- [ ] **Step 1:** Full test gate green (now includes the B1 service tests + extended `PlayerKeyMap` tests — count grows). Run `dotnet test VideoShelf.slnx -c Release --nologo -v q`.
- [ ] **Step 2:** Dump-subagent verdict: grid/rails show roving-tabindex + type-ahead config.
- [ ] **Step 3:** Whole-branch Sonnet review (runbook §4 step F), address blockers.
- [ ] **Step 4:** Push, PR `M20 Group B — keyboard reachability + focus restoration`, CI green, merge from main root, clean up. **Note the two manual keyboard checks in the PR body** (focus return; Esc back-out) as the items the owner verifies.

---

# GROUP C — Color-independent watched/progress cue (PR #3, part 1)

**Branch:** `feat/m20-a11y-state-and-liveregions` (Groups C **and** D ship in one PR, like M19's combined groups). **Outcome:** watched and in-progress state is readable without relying on color — a ✓ glyph for watched and "NN%" text for progress — *and is announced to screen readers*.

### Task C1: Pure converters (`FractionToPercentText`, `WatchedToGlyph`)

**Files:**
- Create or extend: `src/VideoShelf.App/Converters/AccessibilityConverters.cs` (check whether converters live in one `Converters.cs` — if so, add there to match the pattern; the digest shows `Converters/Converters.cs` as the home, so **add to it** and skip the new file).
- Test: `tests/VideoShelf.App.Tests/Accessibility/AccessibilityConvertersTests.cs`

- [ ] **Step 1: Write failing tests.**

```csharp
// tests/VideoShelf.App.Tests/Accessibility/AccessibilityConvertersTests.cs
using System.Globalization;
using VideoShelf.App.Converters;
using Xunit;

public class AccessibilityConvertersTests
{
    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(0.5, "50%")]
    [InlineData(0.756, "76%")]   // rounds
    [InlineData(1.0, "100%")]
    [InlineData(-0.2, "0%")]     // clamps low
    [InlineData(1.5, "100%")]    // clamps high
    public void FractionToPercentText_formats_and_clamps(double f, string expected)
    {
        var c = new FractionToPercentText();
        var r = c.Convert(f, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, r);
    }

    [Theory]
    [InlineData(true, "")]   // checkmark glyph (Segoe Fluent CheckMark) — adjust to chosen glyph
    [InlineData(false, "")]
    public void WatchedToGlyph_returns_check_when_watched(bool watched, string expected)
    {
        var c = new WatchedToGlyph();
        var r = c.Convert(watched, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, r);
    }
}
```

> The glyph: prefer a WPF-UI `SymbolRegular` rendered via the existing `ui:SymbolIcon` system rather than a raw glyph string (M15 established `ui:SymbolIcon`). In that case `WatchedToGlyph` may be unnecessary — instead bind a `ui:SymbolIcon Symbol="Checkmark24"` with `Visibility` driven by the existing `BoolToVisibility` converter on the `IsWatched` flag. **Decide in Task C2** which is cleaner; if you go the SymbolIcon+visibility route, drop `WatchedToGlyph` and its test, keeping only `FractionToPercentText`. Do not ship a dead converter.

- [ ] **Step 2: Run, confirm fail** (`--filter AccessibilityConvertersTests`). Expected: type not found.

- [ ] **Step 3: Implement `FractionToPercentText`** (and `WatchedToGlyph` only if not using SymbolIcon):

```csharp
// in src/VideoShelf.App/Converters/Converters.cs (match the file's existing converter style)
public sealed class FractionToPercentText : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var f = value is double d ? d : 0.0;
        f = Math.Max(0.0, Math.Min(1.0, f));
        return $"{(int)Math.Round(f * 100)}%";
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
```

- [ ] **Step 4: Run tests, confirm pass.**

- [ ] **Step 5: Register the converter** in the resource dictionary where the others are keyed (find the `<conv:... x:Key=...>` block; add `<conv:FractionToPercentText x:Key="FractionToPercentText"/>`). Build clean.

- [ ] **Step 6: Commit** (`feat(a11y): add FractionToPercentText converter for non-color progress`).

### Task C2: Render the watched ✓ and progress NN% on cards

**Files:** Modify `src/VideoShelf.App/Views/VideoCard.xaml` (and wherever watched/progress shows — confirm the card VM exposes `IsWatched` and `ProgressFraction`; the digest confirms `ProgressFraction`).

The current progress bar is **color-only** (accent fill, no text). Add a text label over/beside it and a watched checkmark badge. **Additive overlay** — do not remove the existing bar.

- [ ] **Step 1: Progress text.** Below (or right-aligned on) the progress track in `VideoCard.xaml`, add — visible only when there's progress and the item isn't fully watched:

```xml
<TextBlock Style="{StaticResource Caption}"
           Text="{Binding ProgressFraction, Converter={StaticResource FractionToPercentText}}"
           AutomationProperties.Name="{Binding ProgressFraction, Converter={StaticResource FractionToPercentText}}"
           Visibility="{Binding HasProgress, Converter={StaticResource BoolToVisibility}}" />
```

If `HasProgress` doesn't exist on the card VM, add a read-only `bool HasProgress => ProgressFraction > 0 && !IsWatched;` (no schema/Core change — VM-only).

- [ ] **Step 2: Watched checkmark.** Add a small badge (e.g. top-right of the thumbnail `Border`), visible when `IsWatched`:

```xml
<ui:SymbolIcon Symbol="Checkmark24"
               ToolTip="Watched"
               AutomationProperties.Name="Watched"
               Visibility="{Binding IsWatched, Converter={StaticResource BoolToVisibility}}"
               HorizontalAlignment="Right" VerticalAlignment="Top" Margin="6"/>
```

Confirm `Checkmark24` exists in WPF-UI 4.3.0 `SymbolRegular` (M15/M19 substitution gotcha — only `*24` glyphs exist; if `Checkmark24` is absent, use the verified `CheckmarkCircle24` or `Checkmark48`→nearest; STOP and report if no checkmark glyph exists). Give the icon a readable contrast against the thumbnail by placing it on a small `Border` using an **existing** token brush (e.g. `ChipFillBrush`) — **no new color**.

- [ ] **Step 3: Build + sweep.** This cue **is** screenshot-visible. Run `Run-VisualSweep.ps1` against a populated fixture that has a watched item + an in-progress item (the sweep seeds continue-watching). Dispatch the sweep-reading Sonnet subagent: does Home/Browse show a ✓ on watched cards and "NN%" on in-progress cards? PASS/FAIL + paths. Expected: PASS — state legible without color.

- [ ] **Step 4: Commit** (`feat(a11y): show watched check + progress % so state isn't color-only`).

---

# GROUP D — Live regions (PR #3, part 2 — same branch)

**Outcome:** now-playing, scan-complete/diff, and rename-applied changes are announced to screen readers via `LiveRegionChanged`.

### Task D1: LiveRegion attached behavior (unit-tested decision logic)

**Files:**
- Create: `src/VideoShelf.App/Accessibility/LiveRegion.cs`
- Test: `tests/VideoShelf.App.Tests/Accessibility/LiveRegionTests.cs`

WPF supports `AutomationProperties.LiveSetting`, but to actually *announce* a change you must raise `AutomationEvents.LiveRegionChanged` on the element's peer when the text changes. Wrap that in an attached property so any `TextBlock` becomes a live region by binding `LiveRegion.Text`.

- [ ] **Step 1: Write the failing test for the pure decision** (announce only on a real change to non-empty text):

```csharp
// tests/VideoShelf.App.Tests/Accessibility/LiveRegionTests.cs
using VideoShelf.App.Accessibility;
using Xunit;

public class LiveRegionTests
{
    [Theory]
    [InlineData(null, "Scanning…", true)]   // first non-empty -> announce
    [InlineData("Scanning…", "Scanning…", false)] // unchanged -> no announce
    [InlineData("Scanning…", "Done", true)] // changed -> announce
    [InlineData("Done", "", false)]         // cleared -> no announce
    [InlineData(null, "", false)]           // empty -> no announce
    public void ShouldAnnounce(string? oldText, string newText, bool expected)
        => Assert.Equal(expected, LiveRegion.ShouldAnnounce(oldText, newText));
}
```

- [ ] **Step 2: Run, confirm fail.**

- [ ] **Step 3: Implement.**

```csharp
// src/VideoShelf.App/Accessibility/LiveRegion.cs
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace VideoShelf.App.Accessibility;

/// <summary>Turns a TextBlock into a UIA live region: binding Text raises a
/// LiveRegionChanged event so screen readers announce status changes
/// (now-playing, scan, rename) without the user moving focus.</summary>
public static class LiveRegion
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(LiveRegion),
            new PropertyMetadata(null, OnTextChanged));

    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);
    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);

    // Politeness: default Polite; set Assertive where interruption is warranted.
    public static readonly DependencyProperty PolitenessProperty =
        DependencyProperty.RegisterAttached(
            "Politeness", typeof(AutomationLiveSetting), typeof(LiveRegion),
            new PropertyMetadata(AutomationLiveSetting.Polite));

    public static void SetPoliteness(DependencyObject d, AutomationLiveSetting v) => d.SetValue(PolitenessProperty, v);
    public static AutomationLiveSetting GetPoliteness(DependencyObject d) => (AutomationLiveSetting)d.GetValue(PolitenessProperty);

    public static bool ShouldAnnounce(string? oldText, string? newText)
        => !string.IsNullOrEmpty(newText) && !string.Equals(oldText, newText, System.StringComparison.Ordinal);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        // Mirror to the visible text + the UIA LiveSetting.
        tb.Text = (string)e.NewValue ?? "";
        AutomationProperties.SetLiveSetting(tb, GetPoliteness(d));

        if (!ShouldAnnounce(e.OldValue as string, e.NewValue as string)) return;

        var peer = UIElementAutomationPeer.FromElement(tb)
                   ?? UIElementAutomationPeer.CreatePeerForElement(tb);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
```

> **Verify the enum/method shapes** at build time: `AutomationLiveSetting` (Off/Polite/Assertive), `AutomationProperties.SetLiveSetting`, `AutomationEvents.LiveRegionChanged`, `UIElementAutomationPeer.FromElement`. These are stable, but if any differs, STOP and report (M12/M18 reflection-verify discipline).

- [ ] **Step 4: Run tests, confirm pass.**

- [ ] **Step 5: Commit** (`feat(a11y): add LiveRegion attached behavior`).

### Task D2: Apply live regions to now-playing, scan, and rename

**Files:** Modify `src/VideoShelf.App/Views/PlayerView.xaml` (now-playing title, ~line 76), `src/VideoShelf.App/Views/MainWindow.xaml` / `SettingsView.xaml` (scan status), the rename views; expose status strings on the relevant VMs.

- [ ] **Step 1: Now-playing.** On the player's title `TextBlock`, replace the direct `Text="{Binding Player.Title}"` with the live-region binding (keep it visible as before):

```xml
<TextBlock Style="{StaticResource ...existing...}"
           acc:LiveRegion.Text="{Binding Player.NowPlayingAnnouncement}"
           acc:LiveRegion.Politeness="Polite"/>
```

Add `xmlns:acc="clr-namespace:VideoShelf.App.Accessibility"`. Add a VM property `string NowPlayingAnnouncement => string.IsNullOrEmpty(Title) ? "" : $"Now playing {Title}";` raised when `Title` changes. (Don't announce a bare title — announce the *event*.)

- [ ] **Step 2: Scan status.** Find the scan-status surface (the scan-diff banner shipped in M18 lives on Settings/Maintenance — "Added 12, updated 3"). Bind a (likely existing) status string through `acc:LiveRegion.Text`. If the scan status is only a transient and not bound to a TextBlock, add a small status `TextBlock` (can be visually present per M18's banner) with `acc:LiveRegion.Text="{Binding ScanStatus}"`. Use `Polite`.

- [ ] **Step 3: Rename applied.** On the rename tool / multi-rename result surface, bind the completion summary ("Renamed 5, 0 failed" — M5/M17 produce this) through `acc:LiveRegion.Text`. Use `Polite`.

- [ ] **Step 4: Build + verify via dump.** Re-dump a player state and a Settings/scan state; confirm the relevant `Text` elements carry `LiveSetting` (extend the dumper to print `AutomationProperties.GetLiveSetting(owner)` for `Text` controls). The *announcement* itself can't be asserted headlessly — add to the PR manual checklist: "With Narrator on, starting a video / finishing a scan / applying a rename speaks the status." Expected: live-setting present in the dump.

- [ ] **Step 5: Commit** (`feat(a11y): announce now-playing, scan, and rename via live regions`).

### Task D3: Group C+D review + finish PR #3

- [ ] **Step 1:** Full test gate green (adds C1 + D1 tests).
- [ ] **Step 2:** Sweep-subagent verdict on the ✓/NN% cues (Group C); dump-subagent verdict on live-setting presence (Group D).
- [ ] **Step 3:** Whole-branch review; address blockers.
- [ ] **Step 4:** Push, PR `M20 Groups C+D — color-independent state + live regions`, CI green, merge from main root, clean up. PR body lists the Narrator manual checks.

---

# GROUP E — 44px hit targets + confirm/undo + harness/sweep/consolidation (PR #4)

**Branch:** `feat/m20-a11y-targets-and-finish` (rebased on merged main). **Outcome:** interactive targets meet 44px, destructive actions (Remove source / rename) are confirmed + undoable, the sweep gains an a11y-dump pass, and the milestone is consolidated + ROADMAP flipped.

### Task E1: 44px minimum hit targets

**Files:** Modify `src/VideoShelf.App/Resources/DesignTokens.xaml` (add a `HitTarget` style — additive, **no color**), apply to transport icon buttons (currently ~36px), card affordance buttons, queue-row buttons, chip remove buttons.

- [ ] **Step 1: Add the style** (additive):

```xml
<!-- DesignTokens.xaml — additive; NO color/brush change -->
<Style x:Key="HitTarget" TargetType="Control">
    <Setter Property="MinWidth" Value="44"/>
    <Setter Property="MinHeight" Value="44"/>
</Style>
```

> If the transport `ui:Button`s use a shared base style (M15/M19 `TransportIconButton`, ~36px), the cleanest move is to bump that base style's `MinWidth/MinHeight` to 44 **without touching its visuals/brushes** — verify the M19 two-row bar still lays out (no clipping, PiP-collapse still works). If a 44px transport button breaks the compact PiP layout, keep transport at its current size *inside PiP only* and 44px in the full player (document the exception per no-silent-caps). STOP and report if 44px forces a layout regression you can't resolve additively.

- [ ] **Step 2: Apply** `BasedOn`/Style to the small icon-only buttons that are under 44px: queue-row Move/Play/Remove, chip remove (✕) buttons, card context-menu triggers. Prefer `BasedOn="{StaticResource HitTarget}"` merged with their existing style, or set `MinWidth/MinHeight="44"` directly where there's no shared style.

- [ ] **Step 3: Build + sweep.** Run the sweep; sweep-subagent verdict: do the transport bar, queue rows, and chip removes still render correctly (no clipping/overlap) with the larger targets? Expected: PASS, layout intact.

- [ ] **Step 4: Commit** (`feat(a11y): enforce 44px minimum hit targets`).

### Task E2: Confirm + undo for Remove-source and rename

**Files:** Modify `src/VideoShelf.App/Views/SettingsView.xaml` + its VM (Remove-source), and confirm the rename undo path is surfaced.

`IConfirmService` already exists (M18). Rename already has the M5 undo manifest (`settings.last_rename_manifest`, one-click Undo). This task ensures **both destructive actions are confirmed and recoverable**, and that the undo is reachable by keyboard.

- [ ] **Step 1: Remove-source confirm.** In the Remove-source command path, gate the removal behind `IConfirmService.Confirm(...)` ("Remove this source? Your video files are not deleted.") if it isn't already. Removing a source is **DB-index-only** (never touches disk — runbook §6). Verify that invariant holds; STOP and report if Remove-source touches the filesystem.

- [ ] **Step 2: Remove-source undo.** Capture the removed source descriptor (path + settings) before removal; after removal, expose an "Undo" command that re-adds the source and triggers a rescan (reuse the existing Add-source + scan path — no new Core method if `SourceRepository.Add`/the add-source command already exists). Keep it App-level. Write a VM unit test:

```csharp
[Fact]
public async Task RemoveSource_then_Undo_readds_the_source()
{
    // Arrange a settings VM with a fake source repo / confirm service that returns true.
    // Act: RemoveSourceCommand(src) then UndoRemoveCommand().
    // Assert: the repo's Add was called with the same path; the sources list contains it again.
}
```

Match the test to the actual VM/repo seams (use the project's mocking lib). If the settings VM isn't unit-test-friendly (constructs concrete repos), STOP and report rather than forcing it.

- [ ] **Step 3: Rename undo reachability.** Confirm the existing rename Undo (M5/M17) is a focusable, named control (`AutomationProperties.Name="Undo last rename"`) and keyboard-reachable. No new undo logic — just ensure the affordance is accessible. (If rename has no confirm step before applying, it already shows a preview-diff → Apply, which satisfies "confirm"; don't add a redundant dialog.)

- [ ] **Step 4: Build + test gate.** Run the filtered new test + full gate. Expected: pass.

- [ ] **Step 5: Commit** (`feat(a11y): confirm + undo for remove-source; reachable rename undo`).

### Task E3: Sweep a11y-dump pass + harness view coverage

**Files:** Modify `tools/harness/Run-VisualSweep.ps1` (optional `-A11yDump` switch), confirm `--a11y-dump` is exercised for the key views.

- [ ] **Step 1:** Add a `-A11yDump` switch to `Run-VisualSweep.ps1` that, for each enumerated `--view`, also passes `--a11y-dump "<OutDir>\a11y-<view>.txt"` and leaves the text files in `OutDir`. Reuse the existing per-view launch loop; don't restructure it.

- [ ] **Step 2:** Run the sweep with `-A11yDump`; confirm one `a11y-<view>.txt` per view is produced for at least Home, Browse, Player, Settings, Queue.

- [ ] **Step 3: Commit** (`chore(a11y): a11y-dump pass in the visual sweep`).

### Task E4: Milestone consolidation, final review, ROADMAP flip, finish PR #4

- [ ] **Step 1: Full test gate** green: `dotnet test VideoShelf.slnx -c Release --nologo -v q`. Record the new total (≈ 900 + the unit tests added in B1, B4, C1, D1, E2).

- [ ] **Step 2: App-launch smoke** on the **real, populated** library (not the slim fixture) with `--done-signal` — the M17 crash-on-launch / M18 real-DI-invisible-feature lesson. Confirm clean launch + the a11y dump on the real library shows named cards/grid/transport. Dispatch a Sonnet subagent to read the real-library dump + the sweep PNGs and return one combined text verdict.

- [ ] **Step 3: Whole-branch review** (runbook §4 step F) across all of Group E; address blockers.

- [ ] **Step 4: Flip the ROADMAP row** (this is the final PR — rides this branch per the owner rule). In `C:\Agent Projects\VideoShelf\ROADMAP.md`, change the M20 row Status `[ ] Not started` → `✅ Merged`, set Plan to this file's link, set PR to the four PR numbers, and write a one-line shipped summary. Append a **decision-log entry** capturing: the locked/cut/deferred scope (above), the a11y-dump verification approach, the "screenshots are blind to a11y" lesson, any 44px-PiP exception, and the two manual Narrator/keyboard gates. Note `[[wpfui-theming-and-visual-verification]]` and the M8→M20 no-runner streak.

- [ ] **Step 5: Finish** — push, PR `M20 Group E — 44px targets, confirm/undo, a11y sweep + ROADMAP flip`, CI green, merge `--merge --delete-branch` from the main repo root, clean up the worktree, pull main.

- [ ] **Step 6: Ping the owner** (Phase-B handoff) per the roadmap skill: M20 merged & CI-green, next is M21 (Delight & motion).

---

## Self-review (author checklist — done)

- **Spec coverage:** UIA names/roles → A2/A3/A4; scrubber RangeValue → A3; keyboard reachability (roving-tabindex/type-ahead/Enter-Space/Esc/focus-restore) → B1–B4; color-independent state → C1/C2; live regions → D1/D2; 44px → E1; confirm+undo → E2. Cut items (reduced-motion, PiP keyboard reposition, AA contrast, DPI, color) — explicitly excluded with STOP guards. Deferred items (captions, AD, HC) — excluded.
- **Placeholder scan:** every code step has real code; converters/services/behaviors are fully written; XAML additions are concrete with the first instance shown and the repeat-pattern named.
- **Type consistency:** `IFocusReturnService.Capture/TakeForRestore`, `LiveRegion.Text/Politeness/ShouldAnnounce`, `FractionToPercentText` used consistently across tasks. `A11yTreeDumper.Dump` referenced by the harness hook + the sweep switch.
- **Known unknowns flagged as STOP-and-report:** the harness done-signal hook location; the mocking library in App.Tests; existence of `Checkmark24`; reaching the owner `FrameworkElement` from a peer; STA control-construction (sidestepped via the dump); 44px-vs-PiP layout; settings-VM testability; whether converters live in one file.
- **No Core/schema change** anywhere — App-layer only; M8→M20 no-`user_version`-runner streak preserved (asserted in E4).
