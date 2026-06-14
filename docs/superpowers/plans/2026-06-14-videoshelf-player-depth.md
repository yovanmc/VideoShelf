# M19 — Player depth (plan)

> **Written for Sonnet execution.** If anything here does not match the actual code
> (a signature, a column name, a libVLC API), **STOP and report** rather than guess.
> **Repo:** `C:\Agent Projects\VideoShelf` · default branch `main` · solution `VideoShelf.slnx`.
> **`gh` is NOT on PATH:** call `& "C:\Program Files\GitHub CLI\gh.exe"`.
> **Direct pushes to `main` are blocked** — every change ships via a worktree branch + PR,
> merged `--merge` from the **main repo root** (not the worktree).
> **Test gate:** `dotnet test VideoShelf.slnx -c Release --nologo -v q`
> Current baseline: **808 tests (369 Core + 439 App)** as of M18.
> **Commits:** author `yovanmc` + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. No Codex trailer.
> **Theming rule (load-bearing):** additive only — never retemplate/re-base a `ui:*` (WPF-UI) control's
> Style/Template; player-overlay children must stay **opaque** (M10 transparency-renders-black trap);
> verify every new `SymbolRegular` icon name actually exists (M15 substitution gotcha); off-thread libVLC
> events must marshal to the WPF dispatcher (`Dispatcher.BeginInvoke`).

## Owner decisions (locked via batched `AskUserQuestion`, 2026-06-14)

1. **Redesign FIRST**, then features slot into the new overflow (not onto the old crammed bar). The Films & TV / Media Player minimalist transport redesign is the lead workstream and lands before any feature add.
2. **Full feature set kept**, MINUS the cuts below.
3. **Video chapters: FULL rip-out, backend too** — remove all chapter UI *and* the M12 `video_chapters` table + probe + `ChapterRecord`/`ChapterOption` plumbing. **Duration and resolution probing MUST keep working** (progress bars depend on duration; M18's resolution shares the probe pass). The probe inventory confirms chapter capture is independent of both — see Group A.
4. **Subtitle text styling: DROPPED entirely.** Keep only the M13 gap fix (event-driven track refresh so auto-detected sidecars appear in the track flyout) — that is a track-list bug closure, not styling.
5. **In-video timestamp bookmarks: DROPPED** (no `bookmarks` table; nothing about marking positions inside a video).
6. **Cut: frame-step. Cut: sleep timer.**

### What ships (final M19 scope)

- **A. Transport redesign (lead workstream):** two-row layout · audio+subtitle inline ComboBoxes → one **tracks icon → flyout** (incl. add-subtitle-file) · always-on volume slider → **volume icon → expanding slider** · one **"⋯ More" overflow flyout** for all secondary commands · primary **skip-back 10s / skip-fwd 30s** buttons · slimmer scrim + tighter padding · full cursor+bar **auto-hide on idle** · **click-to-pause** on the video surface.
- **B. libVLC-native features (each lands in the new bar/overflow):** playback **speed 0.5–2×** · **A-B repeat** · **aspect-ratio / zoom presets** · **audio-track picker + volume normalize/boost** (picker exists; normalize is the new, *verify-or-report* part) · end-of-video **Up-Next countdown card** (over the shipped M14 queue; motion polish rides M21) · desktop idioms (**double-click-fullscreen**, **±skip overlay feedback**, **volume-scroll feedback**, **"play from beginning"** on a completed video) · **M13 ESAdded fix** (auto-sidecars appear in the track flyout).

### Explicitly OUT (do not build)

Video chapters (any form) · in-video bookmarks · subtitle text styling · frame-step · sleep timer. If a task seems to require any of these, **STOP and report** — it's a scope error.

## Delivery model

Ship as **stacked PRs split at the GROUP seams below** (the M16/M17/M18 model). **Group A (chapter rip-out) is the load-bearing foundation — do it first** so the redesigned bar is built on a chapter-free player and the destructive removal is verified in isolation. After **Group B** (first new player surface) run an **app-launch smoke** (`HarnessRunner --done-signal`) before proceeding — the M17 crash-on-launch lesson (a latent XAML/resource error rides green CI because unit tests never construct `MainWindow`; the screenshot sweep is the only "does it launch" gate). Each PR: branch in `.worktrees/`, full test gate green, push, PR (here-string body, closing `'@` at column 0), merge `--merge --delete-branch` from the **main repo root**, clean up the worktree.

No `user_version` runner (M8→M18 streak holds). The only schema change is a **removal** (Group A) done with idempotent DDL.

---

## Group A — Chapter full rip-out (Core + App + tests)

**Goal:** Remove every trace of video chapters while leaving duration + resolution probing and the progress bars fully intact. The probe digest confirms duration (`Length/1000`) and resolution (`Size()`/track fallback) are independent of chapter capture (`FullChapterDescriptions`). **HARD GUARD:** after this group, continue-watching progress bars must still render real percentages and resolution-dependent surfaces (M18 duplicate compare) must still work. If at any point removing chapter code appears to also remove duration or resolution capture, **STOP and report** — they are separable.

> **VERIFY FIRST:** grep the repo (`src/` and `tests/`) for `chapter`, `Chapter`, `video_chapters`, `ChapterRecord`, `ChapterOption`, `FullChapterDescriptions`, `NextChapter`, `PreviousChapter`, `ChapterTicks`, `RenderChapterTicks`, `HasChapters`. The known touchpoints are enumerated below; if grep finds a chapter reference NOT listed here, remove it too (same pattern) and note it.

### A1. Core schema — remove the table — `src/VideoShelf.Core/Storage/VideoShelfDb.cs`

- In the `Schema` constant, **delete** the `video_chapters` `CREATE TABLE IF NOT EXISTS` block (the 4-column table: `video_id, idx, name, start_seconds`, PK `(video_id, idx)`, FK→`videos(id) ON DELETE CASCADE`).
- In `Migrate()`, **add one idempotent destructive line** after the existing schema exec / `EnsureColumn` calls:

```csharp
// M19: chapters removed entirely. video_chapters held only DERIVED chapter metadata
// probed from the files (no user-authored data) — dropping it loses nothing recoverable.
conn.Execute("DROP TABLE IF EXISTS video_chapters;");
```

> **Why the DROP is safe (verify-before-destroy):** this table never held user input — user bookmarks were never built; rows were pure libVLC-probed chapter markers derivable from the video files. `DROP TABLE IF EXISTS` is idempotent (safe to run every startup) and removes the now-orphaned table from existing DBs. No other table has an FK to it; nothing reads it after this group. This is the single schema-destructive line in M19; keep it exactly this scoped.

### A2. Core models + repo — `src/VideoShelf.Core/Models/ChapterRecord.cs`, `src/VideoShelf.Core/.../LibraryRepository.cs`

- **Delete** `ChapterRecord.cs` (`record ChapterRecord(int Index, string Name, double StartSeconds)`).
- In `LibraryRepository`, **delete** `ReplaceChapters(...)` and `GetChapters(videoId)` (the only readers/writers of `video_chapters`). Confirm no other call sites remain after deleting their callers (A4, A6).

### A3. Probe — surgical chapter removal — `src/VideoShelf.Core/.../IMediaProbe.cs` + `LibVlcMediaProbe.cs`

- `MediaProbeResult`: **drop the `Chapters` member**. New shape:

```csharp
public sealed record MediaProbeResult(double? DurationSeconds, int? Width, int? Height);
```

- `LibVlcMediaProbe`: **delete only the chapter-capture block** (the `FullChapterDescriptions(-1)` loop building `ChapterRecord`s). **Leave duration (`player.Length`/1000) and resolution (`player.Size(0, ref px, ref py)` + track fallback) untouched.** Update the `return` to the 3-arg `MediaProbeResult(durationSeconds, width, height)`.

> **STOP-and-report** if the chapter loop and the duration/resolution code are interleaved such that they can't be separated cleanly — the digest says they are NOT (chapter lines are a self-contained block), so this should be a clean excision.

### A4. Backfill — `src/VideoShelf.App/Services/MediaBackfillService.cs`

- **Delete the single line** `library.ReplaceChapters(v.Id, r.Chapters);`. Keep `SetDuration(...)` and `SetResolution(...)`. `ResolutionBackfillService.cs` has no chapter references — leave it untouched.

### A5. Engine seam — remove chapter members — `src/VideoShelf.App/Services/IPlaybackEngine.cs`, `LibVlcPlaybackEngine.cs`, `tests/.../FakePlaybackEngine.cs`

- **Delete `ChapterOption`** (defined in `IPlaybackEngine.cs`).
- Remove from `IPlaybackEngine`: `GetChapters()`, `NextChapter()`, `PreviousChapter()`.
- `LibVlcPlaybackEngine`: delete the `GetChapters()` impl (the `FullChapterDescriptions()` reader) + `NextChapter()`/`PreviousChapter()` (`_player.NextChapter/PreviousChapter`).
- `FakePlaybackEngine`: delete the `Chapters` list, `GetChapters()`, and the `NextChapter`/`PreviousChapter` impls (+ any call-count fields).

### A6. PlayerViewModel — `src/VideoShelf.App/ViewModels/PlayerViewModel.cs`

- Delete: the `Chapters` `ObservableCollection`, `HasChapters`, the `NextChapterCommand`/`PreviousChapterCommand` `[RelayCommand]` methods, and in `RefreshTracks()` the chapter-population loop + the `OnPropertyChanged(nameof(HasChapters))`.

### A7. PlayerView — `src/VideoShelf.App/Views/PlayerView.xaml` + `.xaml.cs`

- XAML: delete the `ChapterTicks` `<Canvas>` and both prev/next-chapter `<ui:Button>`s.
- Code-behind: delete `RenderChapterTicks()` and every call to it (`OnLoaded`, `ApplyPipState`, the `HasChapters` branch in `OnPlayerPropertyChanged`). Leave the seek/auto-hide/preview machinery intact.

### A8. Continue-watching chapter label — `DiscoveryViewModel.cs`, `ContinueWatchingCardViewModel.cs`, `MakeContinueCard`, `VideoCard.xaml`

- `DiscoveryViewModel.LoadAsync()`: remove the `contLabels` computation that calls `library.GetChapters(...)`; the continue cards no longer carry a chapter label. Simplify `MakeContinueCard` to the item-only signature and update the `Fill(ContinueWatching, …)` call accordingly.
- `ContinueWatchingCardViewModel`: delete `ChapterLabel` + `HasChapter`.
- `RecencyCardViewModel` already returns null/false — no change.
- `VideoCard.xaml`: delete the `ChapterLabel` `<TextBlock>` (the one gated on `HasChapter`).

### A9. Tests — delete/retarget chapter tests

- **Delete** chapter-only tests: the `ReplaceChapters`/`GetChapters` round-trip test; the two `PlayerViewModel` chapter tests (`RefreshTracks_populates_chapters_…`, `No_chapters_means_HasChapters_false`); the `ChapterOption_carries_index_and_name` contract test.
- **Retarget** `MediaBackfillServiceTests`: rename the duration+chapters test to duration+resolution; drop chapter expectations, keep duration + resolution assertions.
- **Update** the schema-migration test that asserted `video_chapters` exists → assert the table does **NOT** exist after `Migrate()` (a deprecation/removal test proving the DROP ran), while keeping the `duration` column assertion.
- Rename `PlayerTracksAndChaptersTests.cs` → `PlayerTracksTests.cs` (track tests remain).

### Group A acceptance

- Full gate green (test count drops by the deleted chapter tests — expected; the gate is green + sweep PASS, not the number).
- `HarnessRunner --done-signal` writes `OK view=…` for every view (app launches).
- A Sonnet screenshot subagent confirms: **continue-watching cards still show real progress bars** (duration intact), and the player bar renders with **no chapter buttons and no scrubber ticks**. Resolution-dependent M18 surfaces (duplicate compare) still show width×height.

---

## Group B — Transport redesign (the lead workstream)

**Goal:** Rebuild `PlayerView.xaml`'s transport into the Films & TV / Media Player minimalist pattern: progressive disclosure, two-row layout, secondary commands behind one overflow, inline dropdowns replaced by icon→flyout. Additive/theming-rule-safe throughout. This is a **visual** PR — the screenshot sweep is its primary gate.

**Target layout (assemble the XAML to this spec; pixel polish is the implementer's, verified by sweep):**

- **Row 1 (top):** full-width seek `Slider` (`SeekBar`, existing bindings: `Player.ScrubPosition` TwoWay, max `Player.LengthSeconds`, the existing drag handlers + seek-preview popup are kept) with current-time on the left and duration on the right. Keep the seek-preview thumbnail popup. (Chapter ticks are gone per Group A.)
- **Row 2 (controls), three zones:**
  - **Left:** volume icon (`Speaker2_24`/verify) → click toggles an **expanding volume slider** (a `ui:Flyout` or `Popup` anchored to the icon, containing the 0–100 `Slider` bound to `Player.Volume`); not an always-on slider. Optional mute toggle (`ToggleMuteCommand`, see B-VM).
  - **Center:** **skip-back 10s** (`SkipBack10Command`, icon `RewindXxx`/verify or a `History24`-style glyph; choose an existing `SymbolRegular`), **play/pause** (existing `TogglePlayPauseCommand`, the `PlayPauseIcon` Play24↔Pause24 DataTrigger stays), **skip-fwd 30s** (`SkipForward30Command`).
  - **Right:** **tracks icon → flyout** (`ClosedCaption24`): a `ui:Flyout`/`Popup` containing the audio-track list + subtitle-track list + the existing **"+ Sub"** add-subtitle-file button (`AddSubtitleFileCommand`) — this replaces the two inline 140px ComboBoxes. **PiP** (`TogglePictureInPictureCommand`, `PictureInPicture24`), **fullscreen** (`ToggleFullscreenCommand`, `FullScreenMaximize24`), and **"⋯ More"** (`MoreHorizontal24`/`More24` — verify) → overflow flyout.
  - **"⋯ More" overflow flyout** initially contains: **screenshot** (`ScreenshotCommand`), **set-cover** (`SetCoverFromFrameCommand`). Groups D/E add speed, aspect/zoom, audio-normalize, A-B repeat, play-from-beginning into this same flyout. Build the flyout as a vertical list of `TertiaryButton`/menu rows using existing tokens.
- **PiP collapse:** keep the existing "secondary group collapses when `IsPictureInPicture`" behavior — in PiP, show only play/pause + skip + the queue skip-next; collapse the volume/tracks/More/fullscreen group (the digest's `DataTrigger Binding=IsPictureInPicture` pattern). Verify nothing clips at the 360px PiP width (M11 precedent).
- **Scrim + auto-hide:** slim `PlayerScrimBrush`/`PlayerTopScrimBrush` padding so video reads more edge-to-edge (keep brushes **opaque**, M10 trap). Tighten the existing auto-hide so both the bar **and the mouse cursor** hide on idle over the video (the code-behind already has an auto-hide timer + `AreControlsVisible`; extend it to also set `Cursor`); keep the existing suppression rules (don't hide while scrubbing/error/resume-offer).
- **Click-to-pause:** a `MouseLeftButtonDown`/`MouseLeftButtonUp` handler on the video surface that toggles play/pause on a *click* (not a drag and not a double-click — double-click is fullscreen, Group E; guard with a small drag-threshold + a single/double-click discriminator).

**B-VM — new `PlayerViewModel` commands/properties** (`src/VideoShelf.App/ViewModels/PlayerViewModel.cs`):

```csharp
[RelayCommand] private void SkipBack10()    => engine.SeekTo(Math.Max(0, PositionSeconds - 10));
[RelayCommand] private void SkipForward30() => engine.SeekTo(Math.Min(LengthSeconds <= 0 ? PositionSeconds + 30 : LengthSeconds, PositionSeconds + 30));

// optional mute (volume flyout): remember last non-zero volume
[ObservableProperty] private bool isMuted;
private int _volumeBeforeMute = 100;
[RelayCommand] private void ToggleMute() { if (IsMuted) { Volume = _volumeBeforeMute; IsMuted = false; } else { _volumeBeforeMute = Volume == 0 ? 100 : Volume; Volume = 0; IsMuted = true; } }
```

Flyout open/close state is a **view concern** (`ui:Flyout`/`Popup` `IsOpen` toggled by its button) — don't add VM flags for it unless a binding forces it.

**Styles** — add new keyed styles to `src/VideoShelf.App/Resources/` (e.g. a `PlayerStyles.xaml` merged in `App.xaml`, or extend `DesignTokens.xaml`): a flyout container style reusing `PlayerPopupBrush`/`PlayerPopupBorderBrush`, and a transport icon-button style based on `TertiaryButton` + `AppFocusVisual`. **Additive only.** Every icon-only button gets a `ToolTip` + `AutomationProperties.Name` (M15 precedent).

> **STOP-and-report** if any chosen `SymbolRegular` name doesn't exist in the WPF-UI version in use (verify against the icon enum, M15 gotcha) — pick an existing glyph rather than inventing one.

### Group B acceptance

- Full gate green. **App-launch smoke** (`--done-signal` writes `OK view=…` for every view) — run this before Group C.
- Sonnet screenshot sweep on the player (windowed + a PiP-width capture): two-row bar, no inline ComboBoxes, volume behind an icon, tracks flyout opens with audio+subtitle+"+Sub", "⋯ More" opens with screenshot/set-cover, skip-back-10/skip-fwd-30 present, bar+cursor auto-hide on idle, click-to-pause works, no PiP clipping, no transport bleed onto non-player views (M8/M10 regression check).
- *(Optional verification aid, owner-suggested:)* open the same clip in the actual Windows **Media Player** and in VideoShelf and have the screenshot subagent compare the two bars side-by-side for "reads as minimal" parity.

---

## Group C — Engine seam extensions + M13 ESAdded fix (App services + fakes + unit tests)

**Goal:** Extend `IPlaybackEngine` with the libVLC-native capabilities the feature groups need, wire `LibVlcPlaybackEngine` to the real `MediaPlayer` APIs (reflection-verify each first, the M12/M18 pattern), stub `FakePlaybackEngine`, and close the M13 sidecar-refresh gap. Pure/unit-testable against the fake.

**C1. New `IPlaybackEngine` members:**

```csharp
// playback speed (0.5..2.0; libVLC supports wider, UI clamps to this)
double Rate { get; set; }

// aspect-ratio / zoom presets
string? AspectRatio { get; set; }   // null/"" = libVLC default; e.g. "16:9","4:3","1:1","16:10"
float Scale { get; set; }           // 0 = fit-to-window (default); >0 = zoom factor (e.g. 1.0 = 1:1 pixels)

// audio volume normalize/boost (capability-gated; see C3 — VERIFY)
bool SupportsVolumeNormalize { get; }
bool VolumeNormalizeEnabled { get; set; }

// reactive track refresh (M13 fix): raised when libVLC discovers tracks (ESAdded)
event EventHandler? TracksChanged;
```

A-B repeat needs **no** new engine member — it uses the existing `Position`/`SeekTo` and is enforced in the VM (Group E).

**C2. `LibVlcPlaybackEngine` wiring** (reflection-verify each API on LibVLCSharp.WPF **3.9.7.1** before coding — STOP-and-report if a member's shape differs):

- `Rate` → `_player.Rate` (float get/set). Clamp setter input to `[0.5f, 2.0f]`.
- `AspectRatio` → `_player.AspectRatio` (string). `Scale` → `_player.Scale` (float; 0 = auto-fit).
- `TracksChanged`: subscribe `_player.ESAdded += (_, _) => TracksChanged?.Invoke(this, EventArgs.Empty);` in the engine's setup; unsubscribe on dispose. (ESAdded fires on a libVLC thread — the **VM** subscriber marshals to the dispatcher, C4.)

**C3. Volume normalize — VERIFY-OR-REPORT (owner-acknowledged uncertainty):**
libVLC loudness normalization is the `normvol` **audio filter**, typically enabled via a media/LibVLC option (`:audio-filter=normvol` / `--audio-filter=normvol`), not a simple runtime `MediaPlayer` property. Implement it as: `VolumeNormalizeEnabled` setter stores the flag; on the **next `Load`**, if enabled, add `:audio-filter=normvol` (and a sane `:norm-max-level`) to the `Media`'s options before play. `SupportsVolumeNormalize` returns whether the option could be applied.

> **STOP-and-report** if, after wiring, a manual check shows libVLC's Windows output ignores `normvol` (no audible/measurable effect): in that case ship `SupportsVolumeNormalize => false` so Group D **hides** the toggle (no dead control), log the limitation in the PR per the no-silent-caps rule, and we revisit. Do **not** fake a working normalize.

**C4. M13 ESAdded fix — VM subscription** (`PlayerViewModel`): subscribe to `engine.TracksChanged`; in the handler, `Dispatcher.BeginInvoke(RefreshTracks)` (off-thread libVLC event → UI marshal, known gotcha). Unsubscribe on close/dispose. This makes auto-detected sidecars (attached via M13's `Media.AddSlave`) appear in the new tracks flyout without a manual refresh — closing the documented M13 gap.

**C5. `FakePlaybackEngine` stubs** (`tests/VideoShelf.App.Tests/TestSupport/FakePlaybackEngine.cs`): backing fields for `Rate` (default 1.0), `AspectRatio`, `Scale`, `VolumeNormalizeEnabled`; `SupportsVolumeNormalize => true` (so VM tests exercise the toggle path); a `RaiseTracksChanged()` test helper that fires `TracksChanged`.

**C-tests** (App unit tests against the fake): `Rate` clamps to `[0.5,2.0]`; setting `AspectRatio`/`Scale` flows to the engine; `TracksChanged` → VM calls `RefreshTracks` (assert the tracks collections repopulate from the fake); `VolumeNormalizeEnabled` round-trips when supported.

### Group C acceptance

Full gate green; new engine members covered by fake-backed unit tests; app launches (`--done-signal`). No UI yet beyond what Group B shipped — D/E/F wire these in.

---

## Group D — Speed + aspect/zoom + audio normalize (into the bar/overflow)

**Goal:** Surface the Group C capabilities in the redesigned player.

**D-VM (`PlayerViewModel`):**

```csharp
// speed
public IReadOnlyList<double> SpeedPresets { get; } = new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
[ObservableProperty] private double playbackRate = 1.0;           // mirrors engine.Rate
partial void OnPlaybackRateChanged(double v) => engine.Rate = v;
[RelayCommand] private void SetРlaybackRate(double v) => PlaybackRate = v;   // (ASCII name; menu item per preset)
public string RateLabel => PlaybackRate == 1.0 ? "1×" : $"{PlaybackRate:0.##}×";

// aspect / zoom presets
public sealed record AspectPreset(string Label, string? Ratio, float Scale);
public IReadOnlyList<AspectPreset> AspectPresets { get; } = new[]
{
    new AspectPreset("Default", null, 0f),
    new AspectPreset("16:9", "16:9", 0f),
    new AspectPreset("4:3", "4:3", 0f),
    new AspectPreset("Fill", null, 1f),     // tune scale during verification
};
[ObservableProperty] private AspectPreset selectedAspect;        // default = Default
partial void OnSelectedAspectChanged(AspectPreset p) { engine.AspectRatio = p.Ratio; engine.Scale = p.Scale; }
[RelayCommand] private void CycleAspect();                        // next preset, wraps

// audio normalize (only when supported)
public bool CanNormalizeVolume => engine.SupportsVolumeNormalize;
[ObservableProperty] private bool volumeNormalizeEnabled;
partial void OnVolumeNormalizeEnabledChanged(bool v) => engine.VolumeNormalizeEnabled = v;
```

**D-UI:** add to the **"⋯ More" overflow flyout** (Group B): a **speed** row (submenu or inline preset buttons showing `RateLabel`), an **aspect/zoom** row (cycle button showing `SelectedAspect.Label`, or a preset submenu). Add the **normalize** toggle to the **tracks flyout's audio section**, gated on `CanNormalizeVolume` (hidden when false — no dead control). Use existing `Chip`/`ChipToggle`/`TertiaryButton` tokens.

> Keep speed/aspect **ephemeral per playback** (reset to 1×/Default on each `Open`) — no settings persistence in M19 (avoids a settings-key/scope decision; revisit if owner asks).

**D-tests:** speed presets set `engine.Rate`; `RateLabel` formats; `CycleAspect` advances+wraps and pushes ratio/scale to the engine; the normalize toggle is hidden when `SupportsVolumeNormalize` is false.

### Group D acceptance

Full gate green; sweep shows the overflow with speed + aspect rows and (if supported) the normalize toggle in the tracks flyout; changing speed visibly updates `RateLabel`.

---

## Group E — A-B repeat + desktop idioms

**Goal:** A-B loop plus the standard desktop-player idioms.

**E1. A-B repeat (VM, enforced on the existing position tick):**

```csharp
[ObservableProperty] private double? repeatStartSeconds;
[ObservableProperty] private double? repeatEndSeconds;
public bool IsAbRepeatActive => RepeatStartSeconds is { } a && RepeatEndSeconds is { } b && b > a;
[RelayCommand] private void SetRepeatA() => RepeatStartSeconds = PositionSeconds;
[RelayCommand] private void SetRepeatB() { if (RepeatStartSeconds is { } a && PositionSeconds > a) RepeatEndSeconds = PositionSeconds; }
[RelayCommand] private void ClearAbRepeat() { RepeatStartSeconds = null; RepeatEndSeconds = null; }
```

In the existing engine `PositionChanged` handler: if `IsAbRepeatActive` and `PositionSeconds >= RepeatEndSeconds`, `engine.SeekTo(RepeatStartSeconds.Value)`. UI: an **A-B** row in the "⋯ More" flyout (Set A / Set B / Clear, showing the current A/B times); a small on-bar indicator when active.

**E2. Double-click-fullscreen:** a `MouseDoubleClick` handler on the video surface → `ToggleFullscreenCommand`. Reconcile with click-to-pause (Group B): a single click pauses, a double click toggles fullscreen — use WPF's click-count discrimination (suppress the pause when a double-click is detected).

**E3. ±skip overlay feedback:** when `SkipBack10`/`SkipForward30` (or the keyboard ±10s) fire, show a transient badge ("−10s" / "+30s") centered over the video. VM: `[ObservableProperty] string? skipFeedback;` set on skip, cleared by a short `DispatcherTimer` (~700ms). Static badge now; **motion/fade polish is M21** (keep it functional, not animated).

**E4. Volume-scroll feedback:** a `MouseWheel` handler over the video surface adjusts `Volume` by ±5 (clamped 0–100) and shows a transient volume badge (reuse the `SkipFeedback` pattern or a `VolumeFeedback` property). 

**E5. "Play from beginning" on a completed video:** when `Open`-ing a video whose resume is at/near the end or that is marked watched (no meaningful resume), surface a **"Play from beginning"** affordance (a row in the resume banner area and/or the "⋯ More" flyout): `[RelayCommand] private void PlayFromBeginning() { engine.SeekTo(0); CanResume = false; }`. Add a `IsCompleted` computed flag to gate it.

> **Reuse, don't reinvent:** the keyboard ±10s seek already exists in `PlayerView.xaml.cs` `OnKeyDown` — route it through `SkipBack10`/`SkipForward30` so it also triggers the E3 feedback (single source of truth), rather than keeping the inline `+10/-10`.

**E-tests:** A-B activates only when B>A, loops at B→A on the position tick, clears; skip commands clamp at 0/length and set `SkipFeedback`; volume-scroll clamps; `PlayFromBeginning` seeks 0 and dismisses resume; `IsCompleted` gates the affordance.

### Group E acceptance

Full gate green; sweep shows the A-B row + active indicator, a transient skip badge, a transient volume badge, and the "Play from beginning" affordance on a completed item; double-click toggles fullscreen without also pausing.

---

## Group F — End-of-video Up-Next countdown card

**Goal:** Refine the **shipped M14 play-queue** end-of-video behavior into a countdown card (the queue/next-decider is unchanged; this is UI over it). When a video ends and a next item exists (queue-first, else the auto-advance next per `settings.GetAutoAdvanceEpisodes()`), show a card — thumbnail + title + a countdown — then auto-play the next on expiry; **Play now** plays immediately; **Dismiss** cancels (stays on the finished video / closes per current behavior).

**Design (build on the existing `MainViewModel` next-decider + `PlayQueueViewModel.GetNextAfterEnd`):**
- The current flow: `PlayerViewModel.OnEnded` marks watched + raises `PlaybackEnded`; `MainViewModel` decides the next item and opens it immediately. **Change:** instead of opening immediately, if a next item exists, populate an **Up-Next** state and start a countdown; open the next item when the countdown elapses or **Play now** is pressed; **Dismiss** clears it.
- VM (extend `MainViewModel` or add a small `UpNextViewModel` it owns): `UpNextTitle`, `UpNextThumbnailPath` (use the next item's art/placeholder — note the known `RecencyCardViewModel.ThumbnailPath`-is-unpopulated limitation; a placeholder is acceptable, don't overclaim a thumbnail that isn't wired), `CountdownSeconds` (e.g. start at 10), `IsUpNextVisible`, `[RelayCommand] PlayNextNow()`, `[RelayCommand] DismissUpNext()`. Use a `DispatcherTimer` decrementing `CountdownSeconds`; at 0 → open next.
- UI: a card overlay anchored bottom-right of the player (above the queue drawer area), opaque (M10 trap), using existing tokens. **Functional countdown number now; the ring/motion polish is M21** — keep it a text/number countdown, no animated ring.

> **STOP-and-report** if wiring this cleanly requires restructuring `MainViewModel.OpenPlayer`/the `PlaybackEnded` path in a way that risks the M14 single-next-decider invariant (one funnel). Prefer inserting the countdown as a gate *before* the existing open-next call, leaving the decider logic intact.

**F-tests:** when ended with a next item, `IsUpNextVisible` true + countdown set + next NOT yet opened; countdown reaching 0 opens the next exactly once; `PlayNextNow` opens immediately and hides the card; `DismissUpNext` cancels (no open); when ended with **no** next item, no card (existing end behavior).

### Group F acceptance

Full gate green; sweep shows the countdown card on end-of-video with title + countdown + Play-now/Dismiss; auto-advance still works; no double-open.

---

## Group G — Harness, sweep, consolidation

**Goal:** Make every new player state reachable by the harness, run the full sweep, and tidy.

- **Harness** (`HarnessRunner` / launch flags): add/confirm `--view player --play …` paths and any seed needed to exercise: the redesigned bar (tracks flyout, volume flyout, "⋯ More" open), speed set, aspect cycled, A-B active, the skip/volume feedback badges, and the Up-Next countdown card (seed a 2-item queue so end-of-video has a next). Reuse `--seed-demo`. Follow the M18 seed-ordering caveat (seed AFTER the final scan or under a non-scanned source so synthetic items aren't re-marked missing).
- **Full screenshot sweep** across **all views** (Sonnet subagent → text verdict + paths; never load PNGs into the controller). Confirm: no chapter remnants anywhere; the new transport on player + PiP; overflow/flyouts; speed/aspect/normalize; A-B + idioms; Up-Next card; **and a regression pass on the non-player views** (no transport bleed, progress bars still real, M18 maintenance/duplicate surfaces intact). The 3 libVLC player views may carry the documented "Webcam Streams Recorder" Direct3D capture bleed — not a regression.
- **Consolidation:** if any ctor fan-out grew, apply the nullable-trailing-param pattern (M16 precedent). Remove any now-dead converters/styles left by Group A (e.g. an unused chapter-tick brush) — but only if confirmed unused.

### Group G acceptance

Full gate green; sweep PASS on all views; app launches on every view (`--done-signal OK view=…`). Record the final test count in the PR (gate is green + sweep PASS, not the number).

---

## Final acceptance (whole milestone)

- All groups merged; full gate green; sweep PASS on every view; app launches against a **populated real library** (M18 lesson: real-DI-only render/visibility paths are invisible to the slim-DI harness and view-less unit tests — verify the new flyouts/overflow/countdown render under real DI with real data, not just an empty-fixture done-signal).
- Duration-driven progress bars and M18 resolution surfaces still work (chapter rip-out did not collateral-damage them).
- Volume normalize is either working-and-verified or cleanly hidden + logged (no dead control, no overclaim).
- ROADMAP M19 row flipped to ✅ Merged with the PR list + a one-line shipped summary; decision-log entry added (durable facts: the chapter `DROP TABLE` rationale, the `normvol` verify outcome, any STOP-and-report resolutions, the redesign-first ordering, SymbolRegular names chosen).
