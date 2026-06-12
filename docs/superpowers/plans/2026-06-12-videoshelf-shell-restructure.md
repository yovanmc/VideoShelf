# M11 — Shell, Navigation & Settings Restructure (Plan)

> **Written for Sonnet execution.** Each task is bite-sized: implement → write/extend the failing
> test → make it pass → run the gate → commit. **If anything here does not match the real code (a
> signature, member, or XAML block), STOP and report rather than guess.** This is the v3 chrome the
> rest of v3 (M12 personal home, M14 design system) builds into.

## Goal (locked by the 2026-06-12 senior UI/UX review)
Kill the always-on left sidebar; move all library/config controls behind a **gear → dedicated
Settings page**; give the shell **back-navigation + active-nav state** and a **first-run empty-library
CTA**; **collapse the creator-page editorial controls behind an Edit affordance**; **remove the
redundant sidebar Creators list**; and **fix the PiP transport clipping** at small width.

## Pre-locked findings from the code digest (do not re-investigate)
- **"Search returns videos" is ALREADY fully implemented** (M8: `SearchViewModel.VideoResults`,
  `SearchView.xaml` "Videos" group, `LibraryRepository.SearchVideos`). **No work needed** — the
  placeholder `"Search creators and videos…"` is already accurate. Don't touch search.
- **The sidebar CREATORS `ListBox` is disconnected from nav** — changing `Library.SelectedSection`
  does NOT navigate (no `OnSelectedSectionChanged`, no wiring). Navigation to a creator page happens
  only via Browse's `CreatorCard` (`Creators.OpenCreatorRequested`) and `Discovery.SectionOpenRequested`.
  So **removing the sidebar loses no navigation.** `Library.Sections` was used ONLY by that ListBox.
- **No `AppView.Settings` / `SettingsView` exists**; settings = the sidebar `CheckBox` only.
- **DI = all singletons, constructor injection** (`ServiceCollectionExtensions.AddVideoShelf`).
- **The Settings page needs only members already on `MainViewModel`** (`Sources.AddSourceCommand`,
  `Sources.Sources`/`Sources.RemoveSourceCommand`, `Settings.AutoAdvanceEpisodes`,
  `ScanAndReloadCommand`, `IsScanning`) → **host it with NO DataContext override (inherits the window's
  `MainViewModel`)**, so **no new VM, no `MainViewModel` ctor change, no test-factory fan-out.**
- `EnumToVisibility` (key `EnumToVis`) is pure string compare (`CurrentView.ToString() == parameter`),
  so a new `AppView.Settings` just needs `ConverterParameter=Settings`.
- `MainViewModelTestFactory.Create(out MainVmContext ctx)` builds all 10 ctor params; **we add only
  `[RelayCommand]`s + `[ObservableProperty]`s to `MainViewModel` (no ctor change), so the factory and
  the 3 inline construction sites stay untouched.**

## Design decisions (made from the review; documented so the executor doesn't re-decide)
1. **Settings = a nav-gated page** (`AppView.Settings` + `SettingsView`), reached by a **gear button**
   in the top nav, returned-from by the global Back button. Sections: **Library** (sources list +
   Add source + Scan with a scanning indicator + "Last scanned" time) · **Playback** (Auto-play-next
   toggle) · **Appearance** (a disabled placeholder slot for the M14 theme toggle).
2. **Top chrome** = `[‹ Back]  Home  Browse  [Search box ——]  ⚙`, with an **accent underline**
   active-state indicator (Home→Home; Browse→Browse/SectionDetail/RenameTool; ⚙→Settings).
3. **Back-navigation** = a small `Stack<AppView>` in `MainViewModel`; detail/search opens push the
   prior view; top-level nav buttons (Home/Browse/Settings) clear the stack.
4. **Empty-library** = an overlay CTA shown when **no sources** (`Sources.Sources.Count == 0`).
5. **Creator-page Edit mode** = an `IsEditing` toggle on `SectionDetailViewModel`; art actions + the
   tag editor + tag-pill ✕ + the expanded "Rename files…" button are revealed only in edit mode (the
   per-series right-click "Rename files…" context menu stays always-available).
6. **PiP transport** = collapse the secondary controls (Chapter/audio/subtitle/volume/Screenshot/
   Fullscreen/Mini-player) when `IsPictureInPicture`, leaving Play/Pause + seek (Back-to-window/Close
   already live in the PiP top strip) — fits 360px, no clipping.

## Conventions (from the runbook)
- Worktree under `.worktrees/`; branch `feat/shell-restructure`. Gate: `dotnet test VideoShelf.slnx
  -c Release --nologo -v q`. Build quiet: `dotnet build … -v minimal`. `gh` at
  `& "C:\Program Files\GitHub CLI\gh.exe"`. Merge `--merge` from the main repo root. Commit author
  `yovanmc` + trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` (BOM-free messages,
  no Codex trailer). One commit per task.
- **Theming rule (binds):** additive only — no `Style`/`ControlTemplate` override of a WPF-UI control
  for cosmetics. The active-underline is a sibling `Border`; the gear is a WPF-UI `SymbolIcon`.
- Known **single-test parallel flake**: if exactly one *unrelated* test fails, re-run that project in
  isolation to confirm before reporting.

---

## Task 1 — Core: persist "last scanned" time

**Files:** `src/VideoShelf.Core/Storage/SettingsRepository.cs` (+ a test).

Add two methods mirroring the existing `Get/SetAutoAdvanceEpisodes` pattern (key/value settings table,
`$`-prefixed params, `db.Open()` per call). Store as ISO-8601 UTC string under key `last_scan_utc`.
```csharp
/// <summary>Returns the last successful library-scan time (UTC), or null if never scanned.</summary>
public DateTime? GetLastScanUtc()
{
    using var conn = db.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
    cmd.Parameters.AddWithValue("$k", "last_scan_utc");
    var raw = cmd.ExecuteScalar() as string;
    return DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
}

public void SetLastScanUtc(DateTime utc)
{
    using var conn = db.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "INSERT INTO settings(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v";
    cmd.Parameters.AddWithValue("$k", "last_scan_utc");
    cmd.Parameters.AddWithValue("$v", utc.ToString("o"));
    cmd.ExecuteNonQuery();
}
```
> Match the real `settings` table shape and the existing upsert idiom in this file. If the table or
> the existing getter/setter look different, mirror THEM and STOP-and-report if unclear.

**Test** (`tests/VideoShelf.Core.Tests/…` — put beside the existing SettingsRepository tests):
`SetLastScanUtc_then_GetLastScanUtc_roundtrips` (write a known UTC, read it back within 1s tolerance)
and `GetLastScanUtc_is_null_when_never_set`.

**Verify** gate green. **Commit** `M11: persist last-scan time in SettingsRepository`.

---

## Task 2 — SettingsViewModel: expose last-scan text + MarkScanned()

**File:** `src/VideoShelf.App/ViewModels/SettingsViewModel.cs` (ctor already takes `SettingsRepository settings`).

Add:
```csharp
[ObservableProperty]
private string _lastScanText = "Never scanned";

// call from ctor after reading AutoAdvanceEpisodes:
private void RefreshLastScan()
{
    var t = _settings.GetLastScanUtc();
    LastScanText = t is null ? "Never scanned" : $"Last scanned {t.Value.ToLocalTime():g}";
}

/// <summary>Records a completed scan and refreshes the displayed time.</summary>
public void MarkScanned()
{
    _settings.SetLastScanUtc(DateTime.UtcNow);
    RefreshLastScan();
}
```
Call `RefreshLastScan();` at the end of the constructor (so the page shows the stored value on launch).
> `_settings` is the ctor field name in this file; confirm and match it.

**Verify** builds. **Commit** `M11: SettingsViewModel last-scan text + MarkScanned`.

---

## Task 3 — MainViewModel: Settings nav, back-stack, empty-library, scan marks time

**File:** `src/VideoShelf.App/ViewModels/MainViewModel.cs`. **No ctor change.**

### 3a. Enum
```csharp
public enum AppView { Home, Browse, SectionDetail, RenameTool, Search, Settings }
```

### 3b. Back-stack + Settings + empty-library members
Add fields/members:
```csharp
private readonly System.Collections.Generic.Stack<AppView> _backStack = new();
public bool CanGoBack => _backStack.Count > 0;

/// <summary>True at first run / when no source folders are configured (drives the empty-state CTA).</summary>
public bool IsLibraryEmpty => Sources.Sources.Count == 0;

private void PushNav(AppView from)
{
    if (_backStack.Count == 0 || _backStack.Peek() != from)
        _backStack.Push(from);
    OnPropertyChanged(nameof(CanGoBack));
}

private void ClearBack()
{
    _backStack.Clear();
    OnPropertyChanged(nameof(CanGoBack));
}

[RelayCommand]
private void GoBack()
{
    if (_backStack.Count == 0) return;
    CurrentView = _backStack.Pop();
    OnPropertyChanged(nameof(CanGoBack));
}

[RelayCommand]
private void ShowSettings() { ClearBack(); CurrentView = AppView.Settings; }
```
In the **ctor**, after the existing event wiring, subscribe so the empty-state updates when sources change:
```csharp
Sources.Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsLibraryEmpty));
```

### 3c. Make top-level nav clear the back-stack
Change the two existing commands:
```csharp
[RelayCommand] private void ShowHome()   { ClearBack(); CurrentView = AppView.Home; }
[RelayCommand] private void ShowBrowse() { ClearBack(); CurrentView = AppView.Browse; }
```

### 3d. Push on detail/search navigation
- In `OpenSectionAsync`, BEFORE `CurrentView = AppView.SectionDetail;`, add `PushNav(CurrentView);`.
- In `OpenRenameToolAsync`, BEFORE `CurrentView = AppView.RenameTool;`, add `PushNav(CurrentView);`.
- In the ctor's `Search.PropertyChanged` handler, change the body to push first:
```csharp
Search.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(SearchViewModel.Query) && !string.IsNullOrEmpty(Search.Query))
    {
        if (CurrentView != AppView.Search) PushNav(CurrentView);
        CurrentView = AppView.Search;
    }
};
```
- Change the RenameTool close wiring to use Back: `RenameTool.CloseRequested += (_, _) => GoBack();`

### 3e. Scan marks last-scan time; refresh empty-state after loads
In `ScanAndReload()` (inside the `try`, after the reload calls succeed) add `Settings.MarkScanned();`
and `OnPropertyChanged(nameof(IsLibraryEmpty));`. In `InitializeAsync()` after `Sources.Load();` add
`OnPropertyChanged(nameof(IsLibraryEmpty));`.

**Tests** (`tests/VideoShelf.App.Tests/MainViewModelNavigationTests.cs`, reuse `MainViewModelTestFactory`):
1. `ShowSettings_sets_CurrentView_Settings` → `vm.ShowSettingsCommand.Execute(null)`; assert `CurrentView==AppView.Settings`.
2. `OpenSection_then_GoBack_returns_to_prior_view` → from Home, `await vm.OpenSectionAsync(ctx.SectionId)` (assert SectionDetail + `CanGoBack` true), `vm.GoBackCommand.Execute(null)` → `CurrentView==AppView.Home`, `CanGoBack` false.
3. `ShowBrowse_clears_back_stack` → open a section (CanGoBack true), `vm.ShowBrowseCommand.Execute(null)` → `CanGoBack` false, `CurrentView==Browse`.
4. `IsLibraryEmpty_true_when_no_sources` and `…_false_after_source_added` — use the factory's temp DB; assert false after `vm.Sources` has an entry (the factory seeds a source via `ctx`; if it seeds one, assert `IsLibraryEmpty==false`; add a no-source variant if the factory allows — otherwise assert the property reflects `Sources.Sources.Count==0`).
> If `OpenSectionAsync`'s signature/`MainVmContext.SectionId` differ from the digest, STOP and report.

**Verify** gate green. **Commit** `M11: settings nav + back-stack + empty-library state in MainViewModel`.

---

## Task 4 — Converter: EnumSetToVisibility (multi-value active-nav)

**File:** add to `src/VideoShelf.App/Converters/Converters.cs` (same file as `EnumToVisibility`).
```csharp
/// <summary>Visible when the bound enum's name is in the comma-separated ConverterParameter set
/// (e.g. "Browse,SectionDetail,RenameTool"); used for active top-nav highlighting.</summary>
public sealed class EnumSetToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var cur = value?.ToString();
        var set = (p?.ToString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return System.Array.IndexOf(set, cur) >= 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
```
**Verify** builds. **Commit** `M11: EnumSetToVisibility converter for active-nav state`.

---

## Task 5 — Settings page (SettingsView) + DI + harness

### 5a. New `src/VideoShelf.App/Views/SettingsView.xaml` (+ `.xaml.cs`)
A `UserControl` (xmlns `ui`, `conv`, DesignTokens merged like the other views). **No DataContext set on
its instances** — it inherits `MainViewModel`. Bind existing paths.
```xml
<UserControl x:Class="VideoShelf.App.Views.SettingsView" ... xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml" ...>
  <UserControl.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="/VideoShelf.App;component/Resources/DesignTokens.xaml" />
      </ResourceDictionary.MergedDictionaries>
      <conv:BoolToVisibility x:Key="BoolToVisibility" />
    </ResourceDictionary>
  </UserControl.Resources>
  <ScrollViewer VerticalScrollBarVisibility="Auto">
    <StackPanel Margin="24" MaxWidth="720" HorizontalAlignment="Left">
      <TextBlock Text="Settings" FontSize="24" FontWeight="Bold" Margin="0,0,0,16" />

      <!-- Library -->
      <TextBlock Text="LIBRARY" Style="{StaticResource SectionHeader}" />
      <StackPanel Orientation="Horizontal" Margin="0,8,0,8">
        <ui:Button Content="Add source" Command="{Binding Sources.AddSourceCommand}" Appearance="Primary" />
        <ui:Button Content="Scan" Margin="8,0,0,0" Command="{Binding ScanAndReloadCommand}"
                   IsEnabled="{Binding IsScanning, Converter={StaticResource BoolToVisibility}, ConverterParameter=invert}" />
        <ui:ProgressRing Width="20" Height="20" Margin="12,0,0,0" IsIndeterminate="True"
                         Visibility="{Binding IsScanning, Converter={StaticResource BoolToVisibility}}" />
        <TextBlock Text="{Binding Settings.LastScanText}" VerticalAlignment="Center" Opacity="0.75" Margin="12,0,0,0" />
      </StackPanel>
      <ItemsControl ItemsSource="{Binding Sources.Sources}" Margin="0,0,0,16">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <DockPanel Margin="0,3">
              <ui:Button DockPanel.Dock="Right" Content="Remove"
                         Command="{Binding DataContext.Sources.RemoveSourceCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                         CommandParameter="{Binding}" />
              <TextBlock Text="{Binding DisplayName}" VerticalAlignment="Center" Opacity="0.9" />
            </DockPanel>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>

      <!-- Playback -->
      <TextBlock Text="PLAYBACK" Style="{StaticResource SectionHeader}" Margin="0,8,0,0" />
      <CheckBox Margin="0,8,0,16" Content="Auto-play next episode"
                IsChecked="{Binding Settings.AutoAdvanceEpisodes}" />

      <!-- Appearance (placeholder slot for M14 theming) -->
      <TextBlock Text="APPEARANCE" Style="{StaticResource SectionHeader}" />
      <TextBlock Margin="0,8,0,0" Opacity="0.6"
                 Text="Light/dark theme options are coming in a later update." />
    </StackPanel>
  </ScrollViewer>
</UserControl>
```
`.xaml.cs`: standard `public SettingsView() { InitializeComponent(); }`.
> **STOP-and-report checks:** (a) `ui:ProgressRing` exists in the pinned WPF-UI — if not, use an
> indeterminate `ProgressBar`. (b) The `IsEnabled` invert binding above is fragile; SIMPLER and
> preferred: bind `IsEnabled="{Binding IsScanning}"` is wrong (inverts), so instead **add an
> `InverseBool` converter** OR just leave Scan always enabled and rely on the ProgressRing for
> feedback — pick the simplest that builds; do not invent a converter signature that doesn't exist.
> Match the real `SectionHeader` style key (confirmed present in DesignTokens).

### 5b. DI — register the view if views are DI-resolved
In `ServiceCollectionExtensions.AddVideoShelf`, the existing nav views are instantiated by XAML (the
hosts are declared inline), so **no DI entry is needed for `SettingsView`** (it's created by the XAML
host like `DiscoveryView`). Confirm by how the other `views:*` hosts are declared (they have no DI
registration). If the others ARE registered, add `SettingsView` the same way; otherwise nothing here.

### 5c. Harness — real settings page
`src/VideoShelf.App/Harness/HarnessRunner.cs`: change `ShowSettings()` to `=> _main.CurrentView = AppView.Settings;`.

**Verify** builds + gate green. **Commit** `M11: dedicated Settings page (sources/scan/auto-play + theme slot)`.

---

## Task 6 — MainWindow shell: remove sidebar, new top nav (gear/back/active), Settings host, empty-state

**File:** `src/VideoShelf.App/Views/MainWindow.xaml` (+ register the new converter key).

### 6a. Resources — add the converter key (next to the existing `EnumToVis`)
```xml
<conv:EnumSetToVisibility x:Key="EnumSetToVis" />
```

### 6b. Replace the top nav `<Border>` (region (b)) with:
```xml
<Border Grid.Row="0" Background="{StaticResource SubtleFillBrush}"
        BorderBrush="{StaticResource DividerBrush}" BorderThickness="0,0,0,1">
    <Grid Margin="16,6">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" /><!-- back -->
            <ColumnDefinition Width="Auto" /><!-- Home -->
            <ColumnDefinition Width="Auto" /><!-- Browse -->
            <ColumnDefinition Width="*" />   <!-- search -->
            <ColumnDefinition Width="Auto" /><!-- gear -->
        </Grid.ColumnDefinitions>

        <ui:Button Grid.Column="0" Command="{Binding GoBackCommand}" Appearance="Transparent"
                   Margin="0,0,8,0" ToolTip="Back"
                   Visibility="{Binding CanGoBack, Converter={StaticResource BoolToVisibility}}">
            <ui:SymbolIcon Symbol="ArrowLeft24" />
        </ui:Button>

        <Grid Grid.Column="1" Margin="0,0,4,0">
            <ui:Button Content="Home" Command="{Binding ShowHomeCommand}" Appearance="Transparent" />
            <Border Height="2" VerticalAlignment="Bottom" Margin="8,0" Background="{StaticResource AccentBrush}"
                    Visibility="{Binding CurrentView, Converter={StaticResource EnumSetToVis}, ConverterParameter=Home}" />
        </Grid>
        <Grid Grid.Column="2">
            <ui:Button Content="Browse" Command="{Binding ShowBrowseCommand}" Appearance="Transparent" />
            <Border Height="2" VerticalAlignment="Bottom" Margin="8,0" Background="{StaticResource AccentBrush}"
                    Visibility="{Binding CurrentView, Converter={StaticResource EnumSetToVis}, ConverterParameter='Browse,SectionDetail,RenameTool'}" />
        </Grid>

        <ui:TextBox Grid.Column="3" PlaceholderText="Search creators and videos…" Margin="24,0"
                    Text="{Binding Search.Query, UpdateSourceTrigger=PropertyChanged}" />

        <Grid Grid.Column="4">
            <ui:Button Command="{Binding ShowSettingsCommand}" Appearance="Transparent" ToolTip="Settings">
                <ui:SymbolIcon Symbol="Settings24" />
            </ui:Button>
            <Border Height="2" VerticalAlignment="Bottom" Margin="8,0" Background="{StaticResource AccentBrush}"
                    Visibility="{Binding CurrentView, Converter={StaticResource EnumSetToVis}, ConverterParameter=Settings}" />
        </Grid>
    </Grid>
</Border>
```
> If `ui:SymbolIcon` `Symbol` values `ArrowLeft24`/`Settings24` don't exist in the pinned WPF-UI,
> STOP and report (do NOT silently fall back to a text glyph — the gear is the one intentional icon
> this milestone introduces; we want it right). The bindings resolve on the window's `MainViewModel`.

### 6c. Delete the sidebar and collapse the content grid to one column
- **Delete** the entire left sidebar `<Border Grid.Column="0" …>…</Border>` (digest region (c)).
- The main content `<Grid Grid.Row="1">` currently has `ColumnDefinitions` (288 + *). **Remove the
  `<Grid.ColumnDefinitions>`** and **remove `Grid.Column="1"`** from the content `<Grid>` so it fills
  the full width. (The `PlayerHost` already spans the outer grid rows — leave it untouched.)

### 6d. Add the Settings host + the empty-state overlay as the last children of the content `<Grid>`
(immediately after the `SearchView` host, inside the same content grid):
```xml
<!-- Settings page -->
<views:SettingsView
    Visibility="{Binding DataContext.CurrentView, RelativeSource={RelativeSource AncestorType=Window},
                 Converter={StaticResource EnumToVis}, ConverterParameter=Settings}" />

<!-- First-run / empty-library overlay (covers the content area when no sources are configured) -->
<Border Background="{StaticResource ApplicationBackgroundBrush}"
        Visibility="{Binding DataContext.IsLibraryEmpty, RelativeSource={RelativeSource AncestorType=Window},
                     Converter={StaticResource BoolToVisibility}}">
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" MaxWidth="420">
        <TextBlock Text="No sources yet" FontSize="22" FontWeight="SemiBold" HorizontalAlignment="Center" />
        <TextBlock Text="Add a folder of videos to start building your library."
                   TextWrapping="Wrap" TextAlignment="Center" Opacity="0.8" Margin="0,8,0,16" />
        <ui:Button Content="Add a source" Appearance="Primary" HorizontalAlignment="Center"
                   Command="{Binding DataContext.Sources.AddSourceCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
    </StackPanel>
</Border>
```
> Use the real app-background brush key if `ApplicationBackgroundBrush` isn't defined — check
> DesignTokens/App.xaml for the correct dark surface brush; STOP-and-report if none obvious (a solid
> `#FF1B1B1B`-style token likely exists). The overlay must be opaque so it hides the empty hosts beneath.

**Verify** the app builds (`dotnet build src/VideoShelf.App/VideoShelf.App.csproj -c Release -v minimal`)
and the gate is green. **Commit** `M11: shell — remove sidebar, gear/back/active top nav, Settings host, empty-state`.

---

## Task 7 — Creator-page Edit mode + PiP transport responsiveness

### 7a. `SectionDetailViewModel` — IsEditing toggle
`src/VideoShelf.App/ViewModels/SectionDetailViewModel.cs`:
```csharp
[ObservableProperty]
private bool _isEditing;

[RelayCommand]
private void ToggleEdit() => IsEditing = !IsEditing;
```
In `LoadAsync(long sectionId)`, set `IsEditing = false;` near the top (a freshly-opened creator is not in edit mode).

**Test** (`tests/VideoShelf.App.Tests/…SectionDetail…Tests.cs`): `ToggleEdit_flips_IsEditing`
(construct the VM as existing tests do, assert false→true→false) and, if a load test exists,
assert `IsEditing==false` after `LoadAsync`.

### 7b. `SectionDetailView.xaml` — reveal editorial controls only in edit mode
- Add an **Edit toggle** button in the hero (top-right). Place inside the hero `Grid` (digest region),
  as a sibling above the bottom `StackPanel`:
```xml
<ui:Button Content="Edit" Command="{Binding ToggleEditCommand}" Appearance="Transparent"
           Foreground="White" HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,12,16,0">
    <ui:Button.Style>
        <Style TargetType="ui:Button" BasedOn="{StaticResource {x:Type ui:Button}}">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsEditing}" Value="True">
                    <Setter Property="Content" Value="Done" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ui:Button.Style>
</ui:Button>
```
> `BasedOn="{StaticResource {x:Type ui:Button}}"` keeps the WPF-UI default template (additive — we
> only swap Content text, never the template). If that `BasedOn` form errors, drop the `<Style>` and
> just bind `Content` via a converter, or use two buttons toggled by visibility — simplest that builds.
- Wrap the **art actions** (`Set image…` / `Use default`) and the **tag editor** block (the "Add tag…"
  TextBox + "Add tag" button + the Suggestions `ItemsControl`) each with
  `Visibility="{Binding IsEditing, Converter={StaticResource BoolToVisibility}}"`. The **tag pills**
  `ItemsControl` stays always visible; inside its pill `DataTemplate`, set the ✕ remove `Button`'s
  `Visibility="{Binding DataContext.IsEditing, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={StaticResource BoolToVisibility}}"`.
- The series tile's **expanded "Rename files…" button** (NOT the context-menu item): add
  `Visibility="{Binding DataContext.IsEditing, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={StaticResource BoolToVisibility}}"`.
  Leave the right-click `ContextMenu` "Rename files…" always available.
> Ensure `BoolToVisibility` is available as a `{StaticResource}` in this view (it resolves app-wide
> per prior milestones; if the view lacks it, it still resolves from App.xaml — confirm, else add the key).

### 7c. `PlayerView.xaml` — collapse secondary transport controls in PiP
In the bottom transport's **buttons** `StackPanel`, keep `Play/Pause` first, then wrap the REST
(Chapter ◀/▶, audio ComboBox, subtitle ComboBox, Volume Slider, Screenshot, Fullscreen, Mini-player)
in a single grouping `StackPanel` that collapses in PiP:
```xml
<StackPanel Orientation="Horizontal" Margin="0,8,0,0">
    <ui:Button Content="Play/Pause" Command="{Binding Player.TogglePlayPauseCommand}" />
    <StackPanel Orientation="Horizontal">
        <StackPanel.Style>
            <Style TargetType="StackPanel">
                <Style.Triggers>
                    <DataTrigger Binding="{Binding IsPictureInPicture}" Value="True">
                        <Setter Property="Visibility" Value="Collapsed" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </StackPanel.Style>
        <!-- MOVE the existing Chapter buttons, audio/subtitle ComboBoxes, Volume Slider,
             Screenshot, Fullscreen, Mini-player buttons in here UNCHANGED -->
    </StackPanel>
</StackPanel>
```
`{Binding IsPictureInPicture}` resolves on the PlayerView's `MainViewModel` DataContext (confirmed).
This leaves only Play/Pause + the seek row visible at 360px (Back-to-window/Close live in the PiP top
strip), so nothing clips.

**Verify** builds + gate green. **Commit** `M11: creator-page Edit mode + PiP transport collapses secondary controls`.

---

## Task 8 — Harness sweep update + screenshot verification

### 8a. `tools/harness/Run-VisualSweep.ps1` — `$views` map
- The `settings` entry now captures the **real** Settings page (HarnessRunner change in 5c) — it
  already passes `--view Settings`; keep it but add `--seed-demo` is unnecessary (sources come from
  `--folder`). Leave as `@('--view','Settings')`.
- **Add an empty-state capture** (launch with NO `--folder` so `IsLibraryEmpty` is true). Add a new
  entry that overrides the default `--folder` arg: simplest is a dedicated entry the runner treats as
  "no folder". If the script always injects `--folder $Fixtures` for every entry, add a parallel
  one-off invocation OR add `'empty' = @('--view','Home','--no-folder')` and teach the launch loop to
  skip `--folder` when `--no-folder` is present. **If wiring `--no-folder` is non-trivial, SKIP the
  empty-state capture and instead note it for manual verification** — don't block the milestone on it.

### 8b. Run the sweep + verify (Sonnet subagent text verdict — never load PNGs into the controller)
Run `tools/harness/Run-VisualSweep.ps1` **under `pwsh` 7** (the scripts use `?.`; `powershell.exe` 5.1
errors) on an unlocked composited desktop. Dispatch ONE Sonnet subagent to Read the PNGs in the
reported `PNG_DIR` and return PASS/FAIL + paths, against these criteria:
1. **No sidebar on ANY view** — Home/Browse/Search/SectionDetail/RenameTool now use the **full width**;
   the old left SOURCES/CREATORS column is gone.
2. **Top nav** shows a **gear** at the right and (on detail/search views) a **back** chevron at the
   left; the active page has an **accent underline** (Home underlined on Home; Browse underlined on
   Browse AND SectionDetail).
3. **Settings page** (`settings.png`) shows the real page: a "Settings" header, LIBRARY (Add source +
   Scan + last-scanned text + the source list with Remove), PLAYBACK (Auto-play checkbox), APPEARANCE
   (the "coming in a later update" placeholder). **Not** the Home view.
4. **Creator page** (`section-detail.png`) hero shows name + count + tag pills + an **Edit** button,
   with the **Set image…/Use default and the Add-tag editor HIDDEN** (edit mode off by default).
5. **PiP** (`pip.png`) transport is **not clipped** — only Play/Pause + seek visible in the panel
   (plus the Back-to-window/Close top strip); the secondary controls are gone at PiP size.
6. **No regressions**: player full-window immersive still correct; Home rails still render; no transport
   bleed onto non-player views.
(If the empty-state capture exists, also: a centered "No sources yet — Add a source" CTA fills the content.)

**On FAIL** fix via the implementer loop and re-sweep. **Commit** any harness changes
`M11: harness drives the real Settings page (+ optional empty-state) for the sweep`.

---

## Finish (controller)
1. Final gate `dotnet test VideoShelf.slnx -c Release --nologo -v q` — 0 failures (expect ~265+ tests:
   259 baseline + the new SettingsRepository/MainViewModel/SectionDetail tests).
2. Final whole-branch review (fresh Sonnet) over `git diff main..HEAD`: theming-rule compliance (no
   WPF-UI control re-templates — the active underline is a sibling Border; Edit button uses `BasedOn`),
   no cross-thread `ObservableCollection` mutation, the back-stack edge cases (no duplicate pushes,
   top-level clears), and that `MainViewModel`'s ctor is UNCHANGED (so the test factory still compiles).
3. Push `feat/shell-restructure`; open the PR; **foreground** `gh run watch <id> --interval 20
   --exit-status` (sleep ~20s first); merge `--merge --delete-branch` from the main repo root; sync main;
   remove the worktree.
4. **Update `ROADMAP.md`**: flip M11 to ✅ Merged with the PR #, a one-line summary, and an M11-shipped
   decision-log entry (durable facts: Settings page reuses MainViewModel so no ctor fan-out; sidebar
   removal lost no nav because SelectedSection was disconnected; Search-videos was already done;
   EnumSetToVisibility for active-nav; back-stack model; PiP secondary-controls-collapse pattern;
   any STOP-and-report items hit — esp. the `ui:SymbolIcon` symbol names and the ProgressRing/brush keys).
5. **Ping** the handoff for planning **M12 (Personal home & stats)**.

## STOP-and-report triggers (don't guess)
- `ui:SymbolIcon` symbols `Settings24`/`ArrowLeft24`, or `ui:ProgressRing`, absent in the pinned WPF-UI.
- The `ApplicationBackgroundBrush` / `SectionHeader` / `AccentBrush` resource keys not present as named.
- `MainViewModelTestFactory.Create` / `MainVmContext` / `OpenSectionAsync` shapes differing from the digest.
- The `settings` table / existing `Get/SetAutoAdvanceEpisodes` idiom differing from Task 1's assumption.
- Any WPF-UI control needing a full `ControlTemplate` override to achieve the above (it shouldn't — stop if it seems to).
