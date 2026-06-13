# M15 — Design system & visual consistency (dark, owned)

> **Written for Sonnet execution.** Each task is bite-sized and self-contained. **If anything in the codebase does not match what this plan describes, STOP and report rather than guess** — several tasks touch shared XAML and the WPF-UI theming rule is load-bearing.
>
> **THEMING RULE (load-bearing — caused 2 regressions in a sibling project):** This milestone is **additive only**. NEVER override or re-base a WPF-UI themed control's `Style`/`ControlTemplate` for cosmetics. Style buttons via `BasedOn="{StaticResource {x:Type ui:Button}}"` + the control's own `Appearance` property; add visuals via sibling `Border`/adorner/`FocusVisualStyle`, never by retemplating. Before AND after every UI task, scrutinize the **whole frame** (contrast, chrome, spacing) — a cosmetic tweak that re-bases a themed control has twice silently broken unrelated states.

## Scope decisions (owner-locked 2026-06-13, batched `AskUserQuestion`)

- **DARK MODE ONLY — no light theme.** The owner is explicitly *not* interested in a light mode. So this milestone has **NO** light palette, **NO** Settings theme toggle, **NO** `ApplicationThemeManager.Apply`/`SystemThemeWatcher`, **NO** theme persistence, and **NO** `StaticResource→DynamicResource` migration for runtime theme flipping. The "instant runtime switch" answer is **moot** (nothing to switch between). `App.xaml` keeps `<ui:ThemesDictionary Theme="Dark" />` as-is.
- **Single milestone, FULL scope** for everything else: own the borrowed token set + land every UI-review visual-consistency finding (icon system · ONE CreatorCard + ONE VideoCard · button hierarchy · type scale & heading casing · chip-vs-button · focus rings · player into the app's chrome).
- **Owning ≠ renaming.** "Promote/replace the borrowed VideoTriage `DesignTokens.xaml` into an owned token set" = reorganize it into a real, documented VideoShelf design system and **expand** it; **keep every existing resource KEY name** (many views bind `AccentBrush`/`SubtleFillBrush`/`DividerBrush`/`ThumbPlaceholderBrush`/`CardCaptionBrush`/`CardRadius`/`ControlRadius`/`CardImageRadius`/`SectionGap`/`FieldLabelMargin`/`CardGap`/`SectionHeader`/`StatValue`/`Caption`/`ThumbnailImage`). Renaming them would churn every view for zero benefit. Add new keys; rationalize the 6 hardcoded literal brushes to derive from named colors.

## Current-state facts (verified pre-plan — trust these; if a file differs, STOP)

- **App.xaml** merged dicts, in order: `ui:ThemesDictionary Theme="Dark"` → `ui:ControlsDictionary` → `Resources/DesignTokens.xaml` → `Resources/QueueStyles.xaml`. App-level converters (`BoolToVisibility`, `MissingToOpacity`, `EnumToVis`, `EnumSetToVis`, `FractionToWidth`, etc.) are declared in `App.xaml` and resolve as `{StaticResource}` app-wide. WPF-UI **4.3.0**, target `net10.0-windows`.
- **App.xaml.cs** has NO theme init — leave it that way.
- **DesignTokens.xaml** current keys (complete inventory): Colors `AccentColor #5CC8FF`, `SuccessColor #36C98F`, `WarningColor #F5A524`, `DangerColor #F05252`, `NeutralColor #8B93A7`. Brushes `AccentBrush/SuccessBrush/WarningBrush/DangerBrush/NeutralBrush` (→ their colors). Literal brushes (hardcoded, the ones to rationalize): `SuccessTintBrush #3336C98F`, `WarningTintBrush #33F5A524`, `DividerBrush #22000000`, `SubtleFillBrush #0F7F7F7F`, `ThumbPlaceholderBrush #247F7F7F`, `CardCaptionBrush #B0FFFFFF`. Radii/Thickness: `CardRadius 8`, `ControlRadius 4`, `CardImageRadius 10`, `SectionGap 0,24,0,8`, `FieldLabelMargin 0,12,0,4`, `CardGap 0,0,16,16`. Styles: `SectionHeader` (TextBlock, FontSize 11, SemiBold, AccentBrush, Opacity 0.85 — **NOT** caps-cased itself), `StatValue` (24/SemiBold), `Caption` (12/Opacity 0.6), `ThumbnailImage` (UniformToFill + fade-in). No `DynamicResource` anywhere; no implicit (keyless) styles; no `FocusVisualStyle` anywhere (greenfield).
- **SettingsRepository** (`src/VideoShelf.Core/Storage/SettingsRepository.cs`): `GetString(key, fallback)` / `SetString(key, value)` over a `settings(key,value)` table (INSERT…ON CONFLICT UPDATE); typed wrappers like `GetAutoAdvanceEpisodes()`. (No theme key needed — dark only.)
- **SettingsView.xaml** has an APPEARANCE section (≈lines 68-72): a `SectionHeader` "APPEARANCE" + a TextBlock *"Light/dark theme options are coming in a later update."* — this promise is now false; **remove the whole APPEARANCE block** in T2.
- **Player transport** (`Views/PlayerView.xaml`) is **all text-only** `ui:Button`s and ad-hoc hex surfaces (`#B0101010` bottom bar, `#80101010` top bar, `#CC202020` banners, `#E0101010`/`#60FFFFFF` seek-preview popup). Buttons: Play/Pause, "◀ Chapter"/"Chapter ▶", "+ Sub", "Screenshot", "Fullscreen", "Mini-player", "⏭", "☰ Up next", "Back to window", "Close". Audio/Subtitle are `ComboBox`es; Volume is a `Slider`.
- **Top nav** (`MainWindow.xaml`): Back = `ui:SymbolIcon ArrowLeft24` ✓, Settings = `ui:SymbolIcon Settings24` ✓; **Home / Browse / Up next are text-only.**
- **Cards:** `VideoCard.xaml` and `CreatorCard.xaml` are UserControls, both `Width="200"`, no per-site width overrides anywhere. `VideoCard` binds `PlayCommand`, `ThumbnailPath`, `SeriesTitle`, `EpisodeLabel`, `ChapterLabel`, `ProgressFraction` (via `FractionToWidth`), `HasChapter`. `CreatorCard` binds `OpenCommand`, `ImagePath`, `Name`, `VideoCountLabel`.
  - Home rails: Continue-watching → `<views:VideoCard/>` (`ContinueWatchingCardViewModel`); **Recommended-videos → `<views:VideoCard/>` bound to `RecencyCardViewModel`** (already shipping — missing `ProgressFraction`/`ChapterLabel`/`HasChapter` props degrade to empty/hidden, no crash); Recently-added & Recently-watched → **inline Button templates** (also `RecencyCardViewModel`, identical markup to VideoCard minus progress/chapter); Pick-a-tag → `<views:CreatorCard/>`.
  - Browse (`MainWindow.xaml`) & Search (`SearchView.xaml`) → `<views:CreatorCard/>` / `<views:VideoCard/>`, no overrides.
  - SectionDetail series grid → **inline `Border Width="240"`** (a legitimately *different*, accordion-expandable component — NOT a VideoCard; do not force it into VideoCard).
- **Headings:** ALL-CAPS literals via `SectionHeader` style: Settings "LIBRARY"/"PLAYBACK"/"APPEARANCE", PlayerView/QueuePage "UP NEXT", SectionDetail "SERIES". Home/Search rail headers are raw `<TextBlock FontSize="18" FontWeight="SemiBold"/>` in **Title Case**, NOT using any style.
- **Chips:** Home Pick-a-tag = plain WPF `ToggleButton` (no style); SectionDetail hero tag pills = `Border Background="#55FFFFFF" CornerRadius=ControlRadius` + a `✕` Button; Home top-creator stat pills = `Border Background=SubtleFillBrush CornerRadius=12`.
- **Harness:** `HarnessOptions` flags `--folder --data-dir --view --play --done-signal --autostart --seed-demo`; `--view` supports `Home|Browse|Settings|SectionDetail|RenameTool|Player|PiP|Search|Queue`. Sweep = `tests/.../Run-VisualSweep.ps1` (pwsh-7 only) → per-view GDI grab → Sonnet text verdict. **No `--theme` flag needed** (dark only).
- **Test baseline: 295 (132 Core + 163 App).** This milestone is XAML/styles-heavy → near-zero unit-test delta expected; the **screenshot sweep is the primary gate**. Build/test gate: `dotnet test VideoShelf.slnx -c Release --nologo -v q`. `gh` = `& "C:\Program Files\GitHub CLI\gh.exe"`. Work in a worktree under `.worktrees/`; branch `feat/design-system`.

---

## Design system to build (the target — referenced by the tasks below)

### Type ramp (new keyed `TextBlock` styles in DesignTokens.xaml)
Two-tier heading system that *rationalizes* the existing inconsistency into intent:
- **`TypePageTitle`** — FontSize 24, SemiBold (page/hero titles; e.g. creator name on the hero).
- **`TypeRailHeader`** — FontSize 18, SemiBold (the Home/Search rail headers that are currently raw TextBlocks). Title Case.
- **`TypeBody`** — FontSize 14 (default body).
- **`TypeCaption`** — alias/keep existing `Caption` (FontSize 12, Opacity 0.6).
- **`TypeEyebrow`** — small accent label: FontSize 11, SemiBold, `AccentBrush`, Opacity 0.85. **`SectionHeader` is redefined to BasedOn/equal `TypeEyebrow`** (keep the `SectionHeader` key for back-compat). The small ALL-CAPS accent labels ("UP NEXT", "SERIES", section eyebrows) stay caps **deliberately** as eyebrows; the *big* rail headers use `TypeRailHeader` in Title Case. (Net effect: caps are reserved for the small eyebrow tier only — a consistent rule, minimal churn.)

### Semantic surface/text tokens (new brushes — DARK values, additive)
Add named tokens so views stop using ad-hoc hex / opacity hacks. Where a WPF-UI dark theme brush is the natural match, reference it via `DynamicResource` so our surfaces match WPF-UI controls; otherwise own an explicit dark value:
- **`SurfaceBrush`** = `{DynamicResource ApplicationBackgroundBrush}` (app canvas).
- **`CardSurfaceBrush`** = `{DynamicResource ControlFillColorDefaultBrush}` (card/raised fill) — verify this WPF-UI 4.3.0 key exists; if not, fall back to an explicit `#14FFFFFF`. STOP-and-report if neither resolves.
- **`TextPrimaryBrush`** = `{DynamicResource TextFillColorPrimaryBrush}`, **`TextSecondaryBrush`** = `{DynamicResource TextFillColorSecondaryBrush}` (replace scattered `Opacity="0.6/0.7"` text dimming). If these WPF-UI keys don't resolve, fall back to `#FFFFFFFF` / `#B0FFFFFF`.
- **Rationalize the literal brushes** to derive from named colors instead of bare hex: `SuccessTintBrush`/`WarningTintBrush` keep value but add an XML comment noting source color; `DividerBrush` → `#22FFFFFF` (currently `#22000000` = invisible-on-dark — this is a latent bug: a near-black divider on a dark canvas is barely visible; switching to white-alpha makes it actually show. **Flag this as a deliberate fix in the task notes.**); `SubtleFillBrush`/`ThumbPlaceholderBrush`/`CardCaptionBrush` keep values (already alpha-white, fine on dark). Keep all KEY names.
- **Player scrim tokens** (deliberately translucent-dark over video — they stay dark regardless): `PlayerScrimBrush` `#B0101010`, `PlayerTopScrimBrush` `#80101010`, `PlayerPanelBrush` `#CC202020`, `PlayerPopupBrush` `#E0101010`, `PlayerPopupBorderBrush` `#60FFFFFF`. (Same values as today's literals, now named.)

### Card sizing tokens
- **`CardWidth`** = `200.0` (`x:Double`), **`CardThumbHeight`** = `112.0` (16:9 of 200). Cards reference these instead of literal `200`/`112`.

### Button hierarchy (keyed styles, BasedOn ui:Button — additive)
- **`PrimaryButton`** — `BasedOn="{StaticResource {x:Type ui:Button}}"`, set `Appearance="Primary"` + consistent `Padding`/min-height. (Accent-filled CTA: hero "Play all", resume "Resume", Apply in Rename.)
- **`SecondaryButton`** — `Appearance="Secondary"` (default actions).
- **`TertiaryButton`** — `Appearance="Transparent"` (icon-only / low-emphasis: transport buttons, close, queue row actions).
These only set the `Appearance` DP + sizing — **no template override.** Map each existing button to one tier (T3).

### Chip styles (keyed)
- **`Chip`** — a `Border` style: `Background="{StaticResource SubtleFillBrush}"`, `CornerRadius="12"`, `Padding="10,4"`, used for read-only pills (hero tag pills, stat pills). Replaces the hero pill's hardcoded `#55FFFFFF`.
- **`ChipToggle`** — a `ToggleButton` style `BasedOn` the WPF-UI ToggleButton default (verify `{x:Type ui:ToggleButton}` exists; if Pick-a-tag uses a plain `ToggleButton`, base on the framework default and set rounded `Padding`/`Background` additively — do NOT retemplate). For the Pick-a-tag chips. STOP-and-report if a clean additive style isn't possible without retemplating.

### Focus visual (greenfield)
- **`AppFocusVisual`** — a `Style TargetType="Control"` whose `Template` is a `Rectangle` adorner (2px `AccentBrush` stroke, 2px margin, `SnapsToDevicePixels`). Apply via the `FocusVisualStyle` setter on the button/chip styles above. `FocusVisualStyle` is an **adorner overlay**, not a control template — this is additive and theming-rule-safe. (WPF-UI controls already have keyboard focus visuals; this covers the plain-WPF `ToggleButton` chips and `Slider`.)

### Icon mapping (ui:SymbolIcon — WPF-UI 4.3.0 `SymbolRegular`)
Replace text glyphs with `<ui:SymbolIcon Symbol="…" />` (keep `ui:Button`; for buttons with a label, use a `ui:Button` `Icon` or an inner `StackPanel` icon+text — match existing `ui:Button` usage). **Only `ArrowLeft24` & `Settings24` are grep-proven in-repo.** For every other symbol below, the **C# build is the verifier**: a wrong `SymbolRegular` member fails compilation fast — if a name doesn't compile, pick the nearest valid member (IntelliSense/enum) and note the substitution; STOP-and-report only if no reasonable Fluent symbol exists. (Mirrors M11, which verified symbols by reflection.)

| Button (file) | Today | Proposed `Symbol` (fallback) |
|---|---|---|
| Play/Pause (PlayerView) | "Play/Pause" | `Play24` / `Pause24` (toggle by `IsPlaying` trigger) |
| Prev / Next chapter | "◀ Chapter"/"Chapter ▶" | `Previous24` / `Next24` (fallback `ChevronLeft24`/`ChevronRight24`) |
| Skip-to-next "⏭" | emoji | `Next24` (or `SkipForward2420`) |
| +Sub | "+ Sub" | `ClosedCaption24` (keep tooltip "Add subtitle file") |
| Screenshot | "Screenshot" | `Camera24` |
| Fullscreen | "Fullscreen" | `FullScreenMaximize24` |
| Mini-player | "Mini-player" | `PictureInPicture16`/`20` (fallback `ArrowMinimize24`) |
| Up next "☰ Up next" | emoji+text | `List24` (fallback `Navigation24`) + keep "Up next" label |
| Close / Back-to-window | text | `Dismiss24` / `ArrowLeft24` |
| Top nav Home | "Home" | `Home24` + label |
| Top nav Browse | "Browse" | `Apps24` (fallback `Grid24`) + label |
| Top nav Up next | "Up next" | `List24` + label |
| Hero Play all | "▶ Play all" | `PlayCircle24` + label |
| Hero Edit/Done | text | `Edit24` (Edit) / `Checkmark24` (Done) |
| Hero Set image / Use default | text | `Image24` + label |
| Queue row ▲▼▶✕ (QueueStyles) | glyphs | `ChevronUp24`/`ChevronDown24`/`Play24`/`Dismiss24` |

Keep every button's `Command`, `ToolTip`/`AutomationProperties.Name` (add a `ToolTip` where iconifying removes a text label, so the action stays discoverable).

---

## Tasks

> Order: **tokens first** (T1) so every later task references them; then type/headings (T2), buttons (T3), chips+focus (T4), icons (T5), cards (T6), player chrome (T7), harness+sweep (T8). Run `dotnet build src/VideoShelf.App -v minimal` after each XAML-heavy task to catch resource-key/symbol errors early. Keep `dotnet test VideoShelf.slnx -c Release --nologo -v q` green throughout.

### T1 — Own & expand the token system (`Resources/DesignTokens.xaml`)
Rewrite `DesignTokens.xaml` as VideoShelf's owned design system. **Keep every existing key name and value-meaning**; add the new tokens from the design section above. Concretely:
1. Add a header comment block: "VideoShelf design system — owned (no longer borrowed from VideoTriage). Dark-only. Additive; never retemplate WPF-UI controls." Organize into commented sections: COLORS · BRUSHES (semantic) · SURFACES/TEXT · PLAYER SCRIM · RADII & SPACING · CARD SIZING · TYPE RAMP · BUTTONS · CHIPS · FOCUS · IMAGE.
2. Keep all existing colors/brushes/radii/thickness/`SectionHeader`/`StatValue`/`Caption`/`ThumbnailImage`.
3. Add: `SurfaceBrush`, `CardSurfaceBrush`, `TextPrimaryBrush`, `TextSecondaryBrush` (per design section, with the documented fallbacks if a WPF-UI dynamic key doesn't resolve); change `DividerBrush` to `#22FFFFFF` (deliberate visibility fix — note in commit); add player scrim brushes; add `CardWidth`/`CardThumbHeight` (`xmlns:sys="clr-namespace:System;assembly=System.Runtime"`, `<sys:Double x:Key="CardWidth">200</sys:Double>`); add the type-ramp styles (`TypePageTitle`/`TypeRailHeader`/`TypeBody`/`TypeEyebrow`), redefine `SectionHeader` as `BasedOn="{StaticResource TypeEyebrow}"`; add `PrimaryButton`/`SecondaryButton`/`TertiaryButton` (need `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"` in this dict — add it; QueueStyles.xaml already shows the pattern); add `Chip`/`ChipToggle`; add `AppFocusVisual`.
4. **STOP-and-report** if: a WPF-UI dynamic brush key in the fallbacks resolves to nothing AND the fallback also looks wrong; or `{x:Type ui:Button}`/`{x:Type ui:ToggleButton}` base styles aren't found (means the namespace/version differs).
5. Verify: `dotnet build src/VideoShelf.App -v minimal` succeeds (no missing-resource/duplicate-key errors). No view changes yet → existing sweep visuals must be unchanged except the now-visible divider.

### T2 — Type scale & heading casing
1. **SettingsView.xaml:** delete the entire APPEARANCE block (the `SectionHeader` "APPEARANCE" + the "coming in a later update" TextBlock). Leave LIBRARY/PLAYBACK eyebrows on `SectionHeader` (now `TypeEyebrow`).
2. Apply `TypeRailHeader` to the raw `FontSize="18" FontWeight="SemiBold"` rail headers in **DiscoveryView.xaml** (Continue watching, Creators, More to watch, Recently added, Recently watched, Pick a tag) and **SearchView.xaml** (Creators, Videos). Keep their Title-Case text.
3. Leave "UP NEXT" (PlayerView/QueuePageView), "SERIES" (SectionDetail), "LIBRARY"/"PLAYBACK" (Settings) as eyebrows (`SectionHeader`/`TypeEyebrow`) — caps reserved for the eyebrow tier.
4. Optionally replace obvious `Opacity="0.6/0.7"` text-dimming with `Foreground="{StaticResource TextSecondaryBrush}"` where it's clearly secondary text (caption/episode labels) — additive, only where it reads better; don't churn everything.
5. Verify: build; no test impact. Headings render with consistent two-tier hierarchy.

### T3 — Button hierarchy
Map each `ui:Button`/`Button` to a tier by setting `Style="{StaticResource PrimaryButton|SecondaryButton|TertiaryButton}"` (additive; don't touch templates). Primary = hero "Play all", player Resume, Rename Apply, first-run "Add source" CTA. Tertiary = all player transport icon buttons, Close/Back-to-window, queue row actions, ✕ on chips. Secondary = the rest (Set image / Use default / Edit / Scan / Add source in Settings). Where a button currently sets `Appearance="Transparent"` inline, replace with `TertiaryButton`. Build + keep tests green (any test asserting a button's `Appearance` is unlikely; STOP-and-report if one breaks).

### T4 — Chips + focus rings
1. Hero tag pills (SectionDetailView): wrap in the `Chip` Border style; drop the `#55FFFFFF` literal; the inner `✕` Button → `TertiaryButton` + `Dismiss24` icon.
2. Home top-creator stat pills (DiscoveryView): apply `Chip`.
3. Home Pick-a-tag `ToggleButton` → `Style="{StaticResource ChipToggle}"`. If a clean additive ToggleButton style isn't achievable in WPF-UI 4.3.0 without retemplating, **STOP-and-report** (do not retemplate); a fallback acceptable outcome is leaving it as a styled framework `ToggleButton` with rounded padding + `SubtleFillBrush`/selected = `AccentBrush` via triggers.
4. Apply `FocusVisualStyle="{StaticResource AppFocusVisual}"` on the button + chip styles (set it once in the style Setters in T1, so this is verification that keyboard focus shows a visible accent ring on chips/sliders/buttons). Verify by tabbing in the running app during the T8 sweep.

### T5 — Icon system
Apply the icon mapping table (design section) across PlayerView.xaml, MainWindow.xaml (Home/Browse/Up-next nav), SectionDetailView.xaml (hero actions), QueueStyles.xaml. For icon-only buttons, add `ToolTip` + `AutomationProperties.Name` so the action stays discoverable & accessible. For labelled nav buttons, put the icon before the text (a horizontal `StackPanel` or `ui:Button` `Icon`). Play/Pause icon toggles via a `DataTrigger` on the existing `IsPlaying`-equivalent property (find it in `PlayerViewModel`; STOP-and-report if there's no bindable play/pause-state bool). **Build after this task** — symbol-name typos fail compilation; fix per the fallback rule. Keep the PiP-collapse behavior (the secondary-controls group that hides at small width) intact.

### T6 — Unify cards to ONE VideoCard + ONE CreatorCard
1. **Replace the two inline Home templates** (Recently-added, Recently-watched in DiscoveryView.xaml) with `<views:VideoCard/>` — identical to the already-shipping Recommended-videos rail (also `RecencyCardViewModel`). **First STOP-and-report check:** confirm `DiscoveryView.xaml`'s Recommended-videos `ItemsControl` (~line 112) really uses `<views:VideoCard/>` bound to `RecommendedVideos` (`RecencyCardViewModel`); if so, the same control is proven to render `RecencyCardViewModel` (missing `ProgressFraction`/`ChapterLabel`/`HasChapter` → empty/hidden, no crash) and the swap is safe. If that rail does NOT use VideoCard, STOP-and-report.
2. **VideoCard.xaml & CreatorCard.xaml:** replace literal `Width="200"` with `Width="{StaticResource CardWidth}"` and the image `Height="112"` with `Height="{StaticResource CardThumbHeight}"`; set the image `Stretch="UniformToFill"` + `CardImageRadius` clip if not already, for a uniform 16:9 thumb. Apply `CardSurfaceBrush`/`TextSecondaryBrush` where the card uses ad-hoc fills/opacity.
3. **Do NOT** convert the SectionDetail series tile (the inline `Width="240"` accordion tile) into a VideoCard — it's a different, expandable component. Instead standardize its width to `{StaticResource CardWidth}` (200) and apply card radius/surface tokens so it visually matches. (Accept that it remains its own control; the "ONE card each" rule is about the *card* surfaces, which are now uniform.)
4. Verify: build; the existing `BrowseFanoutTests`/discovery tests stay green (no VM change). Sweep (T8) confirms uniform card sizing across Home/Browse/Search.

### T7 — Player into the app's chrome
In PlayerView.xaml replace the ad-hoc hex surfaces with the named scrim tokens from T1: bottom transport bar `#B0101010`→`{StaticResource PlayerScrimBrush}`, top bar `#80101010`→`PlayerTopScrimBrush`, banners `#CC202020`→`PlayerPanelBrush`, seek-preview popup `#E0101010`/`#60FFFFFF`→`PlayerPopupBrush`/`PlayerPopupBorderBrush`. Transport buttons already became `TertiaryButton`+icons (T3/T5). The in-player queue drawer (`#E6101010` from M14) → add a `PlayerDrawerBrush` token or reuse `PlayerPopupBrush` (keep it opaque — the M10 transparency-renders-black trap: do NOT make any player overlay child transparent over the VideoView). Verify: build; sweep confirms the player reads as part of the app (icons + tokenized chrome) and **no transport bleed** onto other views (M10 fix must still hold).

### T8 — Harness sweep + Sonnet text verdict
1. No new harness flag needed. Ensure the sweep covers: Home (cards + rails + chips + stat pills), Browse (creator grid), Search, SectionDetail (`--seed-demo` so hero tag pills render — the M9 lesson), Player (`AutoHideSuppressed` so transport shows — icons + tokenized chrome), PiP (collapsed transport, no bleed), Settings (no APPEARANCE block), Queue/in-player drawer (icon row). Run under **pwsh-7**, unlocked composited desktop, and **close stray always-on-top media windows** (Webcam Streams Recorder / League / flet test patterns) before trusting GDI grabs — the recurring bleed class.
2. Dispatch a **Sonnet subagent** to Read the PNGs and return a **TEXT verdict** (PASS/FAIL + per-view observations + absolute paths). **Do NOT load PNGs into the controller.** Acceptance criteria for the verdict: (a) player transport + nav + hero + queue buttons show **Fluent icons**, not text glyphs; (b) `CreatorCard`/`VideoCard` render at a **uniform size** across Home/Browse/Search (and Recently-added/watched now look identical to Recommended); (c) headings show the **two-tier** system (big Title-Case rail headers + small accent eyebrows), no stray bare ALL-CAPS rail titles; (d) tag/stat **chips** read as chips (rounded fill), distinct from buttons; (e) the **divider** is now visible; (f) keyboard focus shows a **visible accent ring** (tab through during capture or note if not verifiable in a static grab — if not, state so); (g) player chrome uses tokens, **no transport bleed** on non-player views. Fix any FAIL additively and re-sweep.

### Wrap (controller, Phase B mechanics — not a Sonnet code task)
- `dotnet test VideoShelf.slnx -c Release --nologo -v q` green (expect ~295 tests, ±a couple if a button/style assertion needed touching). Push `feat/design-system` → open PR (author `yovanmc` + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`, no Codex trailer) → `gh pr checks <#> --watch` (sleep ~20s first) → `gh pr merge <#> --merge --delete-branch` from the **main repo root** (remove the worktree first if the branch delete fails) → sync main.
- Flip the M15 ROADMAP row to ✅ Merged (PR #, one-line summary) and add a decision-log entry (dark-only scope, owned tokens, the divider fix, any symbol substitutions, sweep findings). The ROADMAP flip rides this feat branch (owner rule).

## Out of scope (do NOT do here)
Light theme / theme toggle / `ApplicationThemeManager` / `SystemThemeWatcher` / theme persistence (owner cut light mode). Any WPF-UI control **retemplating** for cosmetics. Playback-speed and other v4 items. New Core schema (no migration — keep the M8→M14 no-migration streak). Online anything.
