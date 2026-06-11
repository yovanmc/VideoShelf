# VideoShelf App Shell + Library Browse + Thumbnails Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a launchable WPF app on top of the existing `VideoShelf.Core` indexer that lets the user add source folders, scan them read-only into SQLite, and browse Source → Section → Series/Standalone → Episode with thumbnails, search, sort, unwatched-count badges, watched toggles, and missing-file marking.

**Architecture:** Mirror VideoTriage's proven WPF stack — a `WinExe` `VideoShelf.App` project with a generic-host DI bootstrap, CommunityToolkit.Mvvm viewmodels, WPF-UI Fluent/dark theme, and a merged `DesignTokens.xaml`. All logic lives in testable services and viewmodels (xUnit + Shouldly); XAML views are verified only by `dotnet build`. New read-model queries and a small schema addition (`videos.missing`, `videos.added_at`, `videos.resume_position`) land in Core via TDD first. Thumbnails come from bundled libVLC behind an `IThumbnailService` interface whose cache/path/fallback logic is unit-tested with a fake; the concrete libVLC snapshot service stays thin (exercised by the Phase 6 harness).

**Tech Stack:** .NET 10, WPF, WPF-UI 4.3.0, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.Hosting 10.0.8, Microsoft.Data.Sqlite 10.0.0, LibVLCSharp + VideoLAN.LibVLC.Windows, xUnit 2.9.3 + Shouldly 4.3.0.

---

## Conventions (apply to every task)

- **Worktree:** all paths are inside `C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell`. Do not switch branches or touch `main`.
- **Test gate:** `dotnet test VideoShelf.slnx -c Release --nologo -v q` (run from the worktree root). Baseline before this phase: **32 passing Core tests, 0 failures**.
- **TDD per task:** write the failing test → run it (expect fail) → minimal implementation → run it (expect pass) → commit.
- **Commit trailer (every commit):**
  ```
  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  ```
  Git author stays the user's human identity (`yovanmc`). No Codex trailer.
- **Standing principles:** read-only on video files; SQLite owns metadata; migrations are idempotent and crash-safe; self-contained (libVLC only — **no external tools on PATH**, no ffmpeg/HandBrake); no network for content.
- **CWD resets between shell calls** — every command below starts with a `cd` into the worktree.
- **XAML is never unit-tested.** For XAML-only tasks the verification step is `dotnet build VideoShelf.slnx -c Release` succeeding, plus a written "eyeball" note.

---

## File Structure

New production files (all under `src\VideoShelf.App\` unless noted; Core additions under `src\VideoShelf.Core\`):

| File | Responsibility |
|---|---|
| `src\VideoShelf.App\VideoShelf.App.csproj` | WinExe project, NuGet refs, Core ref, `InternalsVisibleTo` |
| `src\VideoShelf.App\App.xaml` / `App.xaml.cs` | WPF-UI dark theme + merged tokens; DI generic-host startup |
| `src\VideoShelf.App\Resources\DesignTokens.xaml` | Ported color/surface/radius/spacing/type tokens |
| `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs` | `AddVideoShelf` DI registration |
| `src\VideoShelf.App\Services\AppPaths.cs` | Resolves `%LOCALAPPDATA%\VideoShelf` paths |
| `src\VideoShelf.App\Services\LibraryBootstrap.cs` | Opens + migrates `VideoShelfDb` |
| `src\VideoShelf.App\Services\IFolderPicker.cs` / `FolderPicker.cs` | Testable folder-picker abstraction + Win32 impl |
| `src\VideoShelf.App\Services\IScanCoordinator.cs` / `ScanCoordinator.cs` | Background scan-all-sources orchestration |
| `src\VideoShelf.App\Services\IThumbnailService.cs` | Thumbnail contract (cache key + fallback) |
| `src\VideoShelf.App\Services\ThumbnailCache.cs` | Disk-cache path/hash/fallback logic (testable) |
| `src\VideoShelf.App\Services\LibVlcThumbnailService.cs` | Thin libVLC snapshot impl |
| `src\VideoShelf.App\ViewModels\SourcesViewModel.cs` | Add/remove/list sources |
| `src\VideoShelf.App\ViewModels\LibraryViewModel.cs` | Root browse VM: sections, search, sort |
| `src\VideoShelf.App\ViewModels\SectionViewModel.cs` | Section card + aggregate unwatched badge |
| `src\VideoShelf.App\ViewModels\SeriesViewModel.cs` | Series card + per-series unwatched badge + thumbnail |
| `src\VideoShelf.App\ViewModels\EpisodeViewModel.cs` | Episode row + watched toggle + missing flag |
| `src\VideoShelf.App\ViewModels\MainViewModel.cs` | Shell VM composing sources + library |
| `src\VideoShelf.App\Views\MainWindow.xaml` / `.cs` | FluentWindow shell |
| `src\VideoShelf.Core\Models\BrowseModels.cs` | `SectionSummary`, `SeriesSummary`, `EpisodeView` read-model records |

New Core methods (TDD'd in Core.Tests first):

- `VideoShelfDb` schema: add `videos.missing`, `videos.added_at`, `videos.resume_position` (idempotent).
- `LibraryRepository.MarkMissingForSource`, `ClearMissing`, `GetSectionSummaries`, `GetSeriesSummaries`, `GetEpisodes`, `Search`.
- `ScanService.ScanSource` extended to clear/set `missing`.

New test files mirror the production files under `tests\VideoShelf.Core.Tests\` and `tests\VideoShelf.App.Tests\`.

---

## Task 1: Scaffold `VideoShelf.App` + `VideoShelf.App.Tests`, host builds

**Files:**
- Create: `src\VideoShelf.App\VideoShelf.App.csproj`
- Create: `src\VideoShelf.App\App.xaml`
- Create: `src\VideoShelf.App\App.xaml.cs`
- Create: `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`
- Create: `src\VideoShelf.App\ViewModels\MainViewModel.cs`
- Create: `src\VideoShelf.App\Views\MainWindow.xaml`
- Create: `src\VideoShelf.App\Views\MainWindow.xaml.cs`
- Create: `tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj`
- Create: `tests\VideoShelf.App.Tests\HostBuildsTests.cs`
- Modify: `VideoShelf.slnx`

- [ ] **Step 1: Create the App project file**

`src\VideoShelf.App\VideoShelf.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\VideoShelf.Core\VideoShelf.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="VideoShelf.App.Tests" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.8" />
    <PackageReference Include="WPF-UI" Version="4.3.0" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <LangVersion>latest</LangVersion>
    <GenerateThemeInfoAttribute>false</GenerateThemeInfoAttribute>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Create `App.xaml` (theme + tokens merge)**

`src\VideoShelf.App\App.xaml`:

```xml
<Application x:Class="VideoShelf.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Dark" />
                <ui:ControlsDictionary />
                <!-- Shared design tokens (color/surface/radius/spacing/type). -->
                <ResourceDictionary Source="/VideoShelf.App;component/Resources/DesignTokens.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

> Note: `DesignTokens.xaml` is created in Task 2. This `App.xaml` will not parse at runtime until then, but it compiles fine; the build in Step 9 only needs the markup to be syntactically valid and the resource to exist by the time the app runs. To keep this task's build green, Task 2's file is created in the same branch before any launch. If `dotnet build` complains about the missing resource during this task, create a placeholder `Resources\DesignTokens.xaml` containing `<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />` now and fill it in Task 2.

- [ ] **Step 3: Create `App.xaml.cs` (DI host)**

`src\VideoShelf.App\App.xaml.cs`:

```csharp
using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoShelf.App.Services;
using VideoShelf.App.Views;

namespace VideoShelf.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddVideoShelf())
                .Build();

            _host.StartAsync().GetAwaiter().GetResult();
            var window = _host.Services.GetRequiredService<MainWindow>();
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "VideoShelf startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            try
            {
                _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch
            {
                // Preserve the original startup failure shown to the user.
            }
            _host?.Dispose();
            _host = null;
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        finally
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
```

- [ ] **Step 4: Create a minimal `MainViewModel`**

`src\VideoShelf.App\ViewModels\MainViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoShelf.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "VideoShelf";
}
```

- [ ] **Step 5: Create a minimal `MainWindow`**

`src\VideoShelf.App\Views\MainWindow.xaml`:

```xml
<ui:FluentWindow x:Class="VideoShelf.App.Views.MainWindow"
                 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
                 Title="VideoShelf"
                 Width="1180"
                 Height="760"
                 MinWidth="900"
                 MinHeight="600"
                 ExtendsContentIntoTitleBar="True"
                 WindowBackdropType="Mica"
                 WindowStartupLocation="CenterScreen">
    <Grid>
        <ui:TitleBar Title="VideoShelf" />
    </Grid>
</ui:FluentWindow>
```

`src\VideoShelf.App\Views\MainWindow.xaml.cs`:

```csharp
using Wpf.Ui.Controls;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
```

- [ ] **Step 6: Create `ServiceCollectionExtensions.AddVideoShelf`**

`src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Views;

namespace VideoShelf.App.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoShelf(this IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
```

- [ ] **Step 7: Create the App.Tests project file**

`tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\VideoShelf.App\VideoShelf.App.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

> `UseWPF` is required so the test project can reference WPF types resolved through the App project. xUnit tests that touch DI run on an STA-free thread fine here because we only build the host and resolve viewmodels (no `Window` instantiation in this task's test).

- [ ] **Step 8: Write the failing test**

`tests\VideoShelf.App.Tests\HostBuildsTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Tests;

public class HostBuildsTests
{
    [Fact]
    public void AddVideoShelf_resolves_main_viewmodel()
    {
        var provider = new ServiceCollection().AddVideoShelf().BuildServiceProvider();

        var vm = provider.GetRequiredService<MainViewModel>();

        vm.Title.ShouldBe("VideoShelf");
    }
}
```

- [ ] **Step 9: Add both projects to the solution**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet sln VideoShelf.slnx add src\VideoShelf.App\VideoShelf.App.csproj tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj
```

Expected: `Project ... added to the solution.` (twice). Confirm `VideoShelf.slnx` now lists both new projects under `/src/` and `/tests/`.

- [ ] **Step 10: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **33 passing tests, 0 failures** (32 Core + 1 App). If the build fails on the missing `DesignTokens.xaml` resource, create the placeholder per Step 2's note and re-run.

- [ ] **Step 11: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App tests\VideoShelf.App.Tests VideoShelf.slnx
git commit -m @'
feat(app): scaffold VideoShelf.App WPF shell + App.Tests

WinExe net10.0-windows project mirroring VideoTriage: WPF-UI dark
theme, generic-host DI bootstrap, AddVideoShelf, minimal MainWindow +
MainViewModel. Host-builds test resolves the root viewmodel.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 2: Port `DesignTokens.xaml`

**Files:**
- Create (or replace placeholder): `src\VideoShelf.App\Resources\DesignTokens.xaml`

- [ ] **Step 1: Write the tokens file**

`src\VideoShelf.App\Resources\DesignTokens.xaml` (ported verbatim from VideoTriage, kept identical for now):

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- ============================================================
         DESIGN TOKENS — single source of truth for color, surface,
         radius, spacing and type. Merged into App.xaml and into each
         view so every view parses self-contained (no reliance on a
         running Application).
         ============================================================ -->

    <!-- Semantic colors (one per meaning) -->
    <Color x:Key="AccentColor">#5CC8FF</Color>
    <Color x:Key="SuccessColor">#36C98F</Color>
    <Color x:Key="WarningColor">#F5A524</Color>
    <Color x:Key="DangerColor">#F05252</Color>
    <Color x:Key="NeutralColor">#8B93A7</Color>

    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}" />
    <SolidColorBrush x:Key="SuccessBrush" Color="{StaticResource SuccessColor}" />
    <SolidColorBrush x:Key="WarningBrush" Color="{StaticResource WarningColor}" />
    <SolidColorBrush x:Key="DangerBrush" Color="{StaticResource DangerColor}" />
    <SolidColorBrush x:Key="NeutralBrush" Color="{StaticResource NeutralColor}" />

    <!-- Faint semantic tints (~20% alpha) for banners/status fills -->
    <SolidColorBrush x:Key="SuccessTintBrush" Color="#3336C98F" />
    <SolidColorBrush x:Key="WarningTintBrush" Color="#33F5A524" />

    <!-- Surface tokens: stroke, subtle fill, thumbnail placeholder -->
    <SolidColorBrush x:Key="DividerBrush" Color="#22000000" />
    <SolidColorBrush x:Key="SubtleFillBrush" Color="#0F7F7F7F" />
    <SolidColorBrush x:Key="ThumbPlaceholderBrush" Color="#247F7F7F" />

    <!-- Corner radii: cards vs. controls/chips -->
    <CornerRadius x:Key="CardRadius">8</CornerRadius>
    <CornerRadius x:Key="ControlRadius">4</CornerRadius>

    <!-- Spacing scale (4px base) for the repeated layout roles -->
    <Thickness x:Key="SectionGap">0,24,0,8</Thickness>
    <Thickness x:Key="FieldLabelMargin">0,12,0,4</Thickness>

    <!-- Typography ramp -->
    <Style x:Key="SectionHeader" TargetType="TextBlock">
        <Setter Property="FontSize" Value="11" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{StaticResource AccentBrush}" />
        <Setter Property="Opacity" Value="0.85" />
    </Style>
    <Style x:Key="StatValue" TargetType="TextBlock">
        <Setter Property="FontSize" Value="24" />
        <Setter Property="FontWeight" Value="SemiBold" />
    </Style>
    <Style x:Key="Caption" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Opacity" Value="0.6" />
    </Style>

    <!-- Thumbnail image: shared fade-in. Opacity defaults to 1 so a
         thumbnail is never stuck hidden if TargetUpdated doesn't fire;
         the From=0 animation only adds a fade when a source arrives. -->
    <Style x:Key="ThumbnailImage" TargetType="Image">
        <Setter Property="Stretch" Value="UniformToFill" />
        <Style.Triggers>
            <EventTrigger RoutedEvent="Binding.TargetUpdated">
                <BeginStoryboard>
                    <Storyboard>
                        <DoubleAnimation Storyboard.TargetProperty="Opacity"
                                         From="0" To="1" Duration="0:0:0.25" />
                    </Storyboard>
                </BeginStoryboard>
            </EventTrigger>
        </Style.Triggers>
    </Style>
</ResourceDictionary>
```

- [ ] **Step 2: Verify the build (no unit test for XAML)**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet build VideoShelf.slnx -c Release --nologo
```

Expected: `Build succeeded`. Eyeball: confirm `App.xaml` references `/VideoShelf.App;component/Resources/DesignTokens.xaml` and the file now exists with real token content (not the placeholder).

- [ ] **Step 3: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\Resources\DesignTokens.xaml
git commit -m @'
feat(app): port DesignTokens.xaml from VideoTriage

Color/surface/radius/spacing/type tokens + shared ThumbnailImage
fade-in style, merged per-view to keep XAML self-contained.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 3: App data location + library bootstrap

**Files:**
- Create: `src\VideoShelf.App\Services\AppPaths.cs`
- Create: `src\VideoShelf.App\Services\LibraryBootstrap.cs`
- Create: `tests\VideoShelf.App.Tests\LibraryBootstrapTests.cs`
- Modify: `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.App.Tests\LibraryBootstrapTests.cs`:

```csharp
using System;
using System.IO;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class LibraryBootstrapTests
{
    [Fact]
    public void OpenLibrary_creates_and_migrates_db_at_given_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vshelf_app_" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(dir);
            var bootstrap = new LibraryBootstrap(paths);

            VideoShelfDb db = bootstrap.OpenLibrary();

            File.Exists(paths.DatabasePath).ShouldBeTrue();
            // A migrated DB can round-trip a source without throwing.
            var repo = new LibraryRepository(db);
            repo.UpsertSource(@"C:\Vids", "Vids");
            repo.GetSources().Count.ShouldBe(1);
            db.Dispose();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void AppPaths_resolves_db_and_thumbs_under_root()
    {
        var paths = new AppPaths(@"C:\Root\VideoShelf");

        paths.DatabasePath.ShouldBe(@"C:\Root\VideoShelf\library.db");
        paths.ThumbnailDirectory.ShouldBe(@"C:\Root\VideoShelf\thumbs");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `AppPaths` / `LibraryBootstrap` do not exist (compile error).

- [ ] **Step 3: Implement `AppPaths`**

`src\VideoShelf.App\Services\AppPaths.cs`:

```csharp
using System;
using System.IO;

namespace VideoShelf.App.Services;

/// <summary>Resolves VideoShelf's on-disk locations. Default root is %LOCALAPPDATA%\VideoShelf.</summary>
public sealed class AppPaths
{
    public string Root { get; }

    public AppPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoShelf"))
    {
    }

    public AppPaths(string root) => Root = root;

    public string DatabasePath => Path.Combine(Root, "library.db");
    public string ThumbnailDirectory => Path.Combine(Root, "thumbs");
}
```

- [ ] **Step 4: Implement `LibraryBootstrap`**

`src\VideoShelf.App\Services\LibraryBootstrap.cs`:

```csharp
using System.IO;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Services;

/// <summary>Ensures the library directory exists, then opens + migrates the SQLite database.</summary>
public sealed class LibraryBootstrap(AppPaths paths)
{
    public VideoShelfDb OpenLibrary()
    {
        Directory.CreateDirectory(paths.Root);
        var db = new VideoShelfDb(paths.DatabasePath);
        db.Migrate(); // idempotent
        return db;
    }
}
```

- [ ] **Step 5: Register in DI**

In `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`, replace the body of `AddVideoShelf` with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using VideoShelf.App.ViewModels;
using VideoShelf.App.Views;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoShelf(this IServiceCollection services)
    {
        services.AddSingleton<AppPaths>();
        services.AddSingleton<LibraryBootstrap>();
        services.AddSingleton<VideoShelfDb>(sp =>
            sp.GetRequiredService<LibraryBootstrap>().OpenLibrary());
        services.AddSingleton<LibraryRepository>();
        services.AddSingleton<WatchRepository>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **35 passing tests** (32 Core + 3 App). `LibraryRepository`/`WatchRepository` ctors take `VideoShelfDb db`, so DI resolves them automatically.

- [ ] **Step 7: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\Services tests\VideoShelf.App.Tests
git commit -m @'
feat(app): app-data paths + library bootstrap

AppPaths resolves %LOCALAPPDATA%\VideoShelf\{library.db,thumbs};
LibraryBootstrap opens + migrates the DB. Wired into DI alongside
the Library/Watch repositories.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 4: Core schema additions — `missing`, `added_at`, `resume_position` (idempotent)

This is a Core change. The existing schema uses `CREATE TABLE IF NOT EXISTS`, so for an **existing** DB the new columns must be added via crash-safe, idempotent `ALTER TABLE` guarded by a column-existence check (`ALTER TABLE` has no `IF NOT EXISTS` in SQLite).

**Files:**
- Modify: `src\VideoShelf.Core\Storage\VideoShelfDb.cs`
- Create: `tests\VideoShelf.Core.Tests\Storage\SchemaMigrationTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.Core.Tests\Storage\SchemaMigrationTests.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Shouldly;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class SchemaMigrationTests
{
    private static HashSet<string> VideoColumns(TempDb temp)
    {
        using var conn = temp.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(videos)";
        var cols = new HashSet<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) cols.Add(r.GetString(1)); // column 1 = name
        return cols;
    }

    [Fact]
    public void Migrate_adds_missing_added_at_and_resume_position_columns()
    {
        using var temp = new TempDb();

        var cols = VideoColumns(temp);

        cols.ShouldContain("missing");
        cols.ShouldContain("added_at");
        cols.ShouldContain("resume_position");
    }

    [Fact]
    public void Migrate_is_idempotent_when_run_twice()
    {
        using var temp = new TempDb();

        // Second migrate must not throw "duplicate column".
        Should.NotThrow(() => temp.Db.Migrate());

        var cols = VideoColumns(temp);
        cols.ShouldContain("missing");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.Core.Tests\VideoShelf.Core.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `cols.ShouldContain("missing")` fails (column not present).

- [ ] **Step 3: Implement the idempotent column additions**

In `src\VideoShelf.Core\Storage\VideoShelfDb.cs`, (a) add `added_at` default to the fresh-create `videos` table so new DBs get it inline, and (b) add a guarded `ALTER TABLE` pass for existing DBs. Replace the `Migrate()` method and the `videos` CREATE block:

Replace the `videos` table definition inside `Schema`:

```csharp
        CREATE TABLE IF NOT EXISTS videos (
            id INTEGER PRIMARY KEY,
            series_id INTEGER NOT NULL REFERENCES series(id) ON DELETE CASCADE,
            file_path TEXT NOT NULL UNIQUE,
            episode_no INTEGER NOT NULL,
            raw_filename TEXT NOT NULL,
            format TEXT NOT NULL,
            duration REAL,
            thumbnail_path TEXT,
            watched INTEGER NOT NULL DEFAULT 0,
            missing INTEGER NOT NULL DEFAULT 0,
            added_at TEXT NOT NULL DEFAULT '',
            resume_position REAL
        );
```

Replace the `Migrate()` method with:

```csharp
    public void Migrate()
    {
        using var conn = Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = Schema;
            cmd.ExecuteNonQuery();
        }

        // Idempotent, crash-safe additions for databases created by an earlier schema.
        // ALTER TABLE ADD COLUMN has no IF NOT EXISTS in SQLite, so guard on table_info.
        EnsureColumn(conn, "videos", "missing", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "videos", "added_at", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "videos", "resume_position", "REAL");
        CreateAddedAtIndex(conn);
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string definition)
    {
        bool exists;
        using (var check = conn.CreateCommand())
        {
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info($t) WHERE name = $c";
            check.Parameters.AddWithValue("$t", table);
            check.Parameters.AddWithValue("$c", column);
            exists = (long)check.ExecuteScalar()! > 0;
        }
        if (exists) return;

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    private static void CreateAddedAtIndex(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS ix_videos_added_at ON videos(added_at)";
        cmd.ExecuteNonQuery();
    }
```

> `table` and `column` here are hardcoded literals (never user input), so interpolating them into the `ALTER`/index DDL is safe — SQLite cannot parameterize identifiers.

- [ ] **Step 4: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **37 passing tests** (34 Core + 3 App). The pre-existing repository/scan tests still pass (new columns have defaults).

- [ ] **Step 5: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.Core\Storage\VideoShelfDb.cs tests\VideoShelf.Core.Tests\Storage\SchemaMigrationTests.cs
git commit -m @'
feat(core): add videos.missing/added_at/resume_position columns

Idempotent, crash-safe migration: fresh DBs get the columns inline;
existing DBs get guarded ALTER TABLE ADD COLUMN (column-existence
check, since SQLite ALTER has no IF NOT EXISTS). Indexes added_at.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 5: Core — stamp `added_at` on insert; expose it on `Video`

`UpsertVideo` currently does not set `added_at`. New videos should record an insert timestamp (preserved on re-upsert). The `Video` record gains `AddedAt` and `Missing`.

**Files:**
- Modify: `src\VideoShelf.Core\Models\Video.cs`
- Modify: `src\VideoShelf.Core\Storage\LibraryRepository.cs`
- Create: `tests\VideoShelf.Core.Tests\Storage\AddedAtTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.Core.Tests\Storage\AddedAtTests.cs`:

```csharp
using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class AddedAtTests
{
    [Fact]
    public void UpsertVideo_stamps_added_at_on_first_insert()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);
        var seriesId = repo.UpsertSeries(repo.UpsertSection(repo.UpsertSource(@"C:\V", "V"), "S"), "Base", false);

        repo.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");

        var v = repo.GetVideosForSeries(seriesId).Single();
        v.AddedAt.ShouldNotBeNullOrEmpty();
        v.Missing.ShouldBeFalse();
    }

    [Fact]
    public void Rescan_preserves_original_added_at()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);
        var seriesId = repo.UpsertSeries(repo.UpsertSection(repo.UpsertSource(@"C:\V", "V"), "S"), "Base", false);

        repo.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        var first = repo.GetVideosForSeries(seriesId).Single().AddedAt;

        repo.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 2, ".mp4"); // re-upsert
        var second = repo.GetVideosForSeries(seriesId).Single().AddedAt;

        second.ShouldBe(first); // added_at is not overwritten
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.Core.Tests\VideoShelf.Core.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `Video` has no `AddedAt`/`Missing` (compile error).

- [ ] **Step 3: Extend the `Video` record**

`src\VideoShelf.Core\Models\Video.cs`:

```csharp
namespace VideoShelf.Core.Models;
public sealed record Video(
    long Id, long SeriesId, string FilePath, int EpisodeNo, string RawFilename,
    string Format, double? Duration, string? ThumbnailPath, bool Watched,
    string AddedAt, bool Missing);
```

- [ ] **Step 4: Set `added_at` in `UpsertVideo` and read both columns in `GetVideosForSeries`**

In `src\VideoShelf.Core\Storage\LibraryRepository.cs`, replace `UpsertVideo`:

```csharp
    public long UpsertVideo(long seriesId, string filePath, int episodeNo, string format)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO videos(series_id, file_path, episode_no, raw_filename, format, added_at, missing)
            VALUES($s, $p, $e, $r, $f, $at, 0)
            ON CONFLICT(file_path) DO UPDATE SET series_id=excluded.series_id,
                episode_no=excluded.episode_no, raw_filename=excluded.raw_filename,
                format=excluded.format
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$s", seriesId);
        cmd.Parameters.AddWithValue("$p", filePath);
        cmd.Parameters.AddWithValue("$e", episodeNo);
        cmd.Parameters.AddWithValue("$r", System.IO.Path.GetFileName(filePath));
        cmd.Parameters.AddWithValue("$f", format);
        cmd.Parameters.AddWithValue("$at", System.DateTimeOffset.UtcNow.ToString("o"));
        return (long)cmd.ExecuteScalar()!;
    }
```

> The `DO UPDATE` clause intentionally omits `added_at` and `missing`, so a re-scan preserves the original insert timestamp. (Task 7's scan clears `missing` separately.)

Replace `GetVideosForSeries` to read the two new columns:

```csharp
    public IReadOnlyList<Video> GetVideosForSeries(long seriesId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, series_id, file_path, episode_no, raw_filename, format, duration,
                   thumbnail_path, watched, added_at, missing
            FROM videos WHERE series_id=$s ORDER BY episode_no
            """;
        cmd.Parameters.AddWithValue("$s", seriesId);
        var list = new List<Video>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Video(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetString(4),
                r.GetString(5), r.IsDBNull(6) ? null : r.GetDouble(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.GetInt64(8) != 0,
                r.IsDBNull(9) ? "" : r.GetString(9), r.GetInt64(10) != 0));
        return list;
    }
```

- [ ] **Step 5: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **39 passing tests** (36 Core + 3 App).

- [ ] **Step 6: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.Core\Models\Video.cs src\VideoShelf.Core\Storage\LibraryRepository.cs tests\VideoShelf.Core.Tests\Storage\AddedAtTests.cs
git commit -m @'
feat(core): stamp added_at on video insert; expose AddedAt/Missing

UpsertVideo records an ISO-8601 insert timestamp, preserved across
re-upserts. Video record gains AddedAt + Missing read from the DB.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 6: Core — missing-file marking on scan

After a scan visits a source, any video under that source whose file is no longer on disk gets `missing=1`; videos found this scan get `missing=0`. We implement two repository methods and call them from `ScanService`.

**Files:**
- Modify: `src\VideoShelf.Core\Storage\LibraryRepository.cs`
- Modify: `src\VideoShelf.Core\Scanning\ScanService.cs`
- Create: `tests\VideoShelf.Core.Tests\Scanning\MissingFileTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.Core.Tests\Scanning\MissingFileTests.cs`:

```csharp
using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Scanning;

public class MissingFileTests
{
    [Fact]
    public void Rescan_marks_deleted_file_missing_then_clears_when_restored()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var fileA = dir.Touch("Creator A/Cool Story.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);
        scan.ScanSource(dir.Path, "My Videos");

        var sourceId = lib.GetSources().Single().Id;
        var section = lib.GetSections(sourceId).Single();
        var series = lib.GetSeriesForSection(section.Id).Single();

        // Delete one episode file on disk, then rescan.
        File.Delete(fileA);
        scan.ScanSource(dir.Path, "My Videos");

        var afterDelete = lib.GetVideosForSeries(series.Id);
        afterDelete.Single(v => v.FilePath == fileA).Missing.ShouldBeTrue();
        afterDelete.Single(v => v.FilePath != fileA).Missing.ShouldBeFalse();

        // Restore the file, rescan: missing flag clears.
        dir.Touch("Creator A/Cool Story.mp4");
        scan.ScanSource(dir.Path, "My Videos");

        lib.GetVideosForSeries(series.Id)
            .Single(v => v.FilePath == fileA).Missing.ShouldBeFalse();
    }
}
```

> Note: deleting `Cool Story.mp4` leaves `Cool Story 2.mp4`, so the grouping/series is unchanged and the missing row stays attached to the same series. The DB still holds the deleted file's row (we never auto-remove), so `GetVideosForSeries` returns both.

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.Core.Tests\VideoShelf.Core.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — both videos report `Missing == false` after the delete (no missing logic yet).

- [ ] **Step 3: Add repository methods**

Append to `src\VideoShelf.Core\Storage\LibraryRepository.cs` (inside the class):

```csharp
    /// <summary>Marks every video under the given source as missing (a scan will clear the ones it finds).</summary>
    public void MarkAllMissingForSource(long sourceId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE videos SET missing = 1
            WHERE series_id IN (
                SELECT se.id FROM series se
                JOIN sections sc ON sc.id = se.section_id
                WHERE sc.source_id = $src)
            """;
        cmd.Parameters.AddWithValue("$src", sourceId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Clears the missing flag for a single video by file path (called when a scan finds it).</summary>
    public void ClearMissing(string filePath)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE videos SET missing = 0 WHERE file_path = $p";
        cmd.Parameters.AddWithValue("$p", filePath);
        cmd.ExecuteNonQuery();
    }
```

- [ ] **Step 4: Wire missing-marking into `ScanService`**

Replace `src\VideoShelf.Core\Scanning\ScanService.cs`:

```csharp
using System.IO;
using System.Linq;
using VideoShelf.Core.Naming;
using VideoShelf.Core.Storage;

namespace VideoShelf.Core.Scanning;

/// <summary>
/// Orchestrates a full source scan: discover sections/files, group into series/standalones,
/// and upsert into the library. Idempotent — re-scanning the same source updates in place
/// (upserts keyed by natural keys), so watched-state and IDs survive. Videos no longer found
/// on disk are marked missing (never deleted from the index); found videos clear the flag.
/// </summary>
public sealed class ScanService(VideoShelfDb db, LibraryRepository library)
{
    public void ScanSource(string sourceRoot, string displayName)
    {
        var sourceId = library.UpsertSource(sourceRoot, displayName);

        // Tentatively mark everything under this source missing; clear each file we re-find.
        library.MarkAllMissingForSource(sourceId);

        foreach (var section in FolderScanner.Scan(sourceRoot))
        {
            var sectionId = library.UpsertSection(sourceId, section.FolderName);
            var grouped = SectionGrouper.Group(section.Files.Select(f => f.FileName).ToList());

            foreach (var series in grouped.Series)
            {
                var seriesId = library.UpsertSeries(sectionId, series.BaseTitle, series.IsStandalone);
                foreach (var episode in series.Episodes)
                {
                    var full = Path.Combine(sourceRoot, section.FolderName, episode.FileName);
                    library.UpsertVideo(seriesId, full, episode.EpisodeNumber, Path.GetExtension(episode.FileName));
                    library.ClearMissing(full);
                }
            }
        }
    }
}
```

> `UpsertVideo` inserts new rows with `missing=0` and (per Task 5) does not touch `missing` on conflict; the explicit `MarkAllMissingForSource` + `ClearMissing` pair is what flips existing rows. `ClearMissing` after upsert is harmless for brand-new rows and authoritative for re-found ones.

- [ ] **Step 5: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **40 passing tests** (37 Core + 3 App). The existing `Rescan_is_idempotent_and_preserves_watched_state` test still passes (its file is never deleted, so it stays `missing=0`).

- [ ] **Step 6: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.Core\Storage\LibraryRepository.cs src\VideoShelf.Core\Scanning\ScanService.cs tests\VideoShelf.Core.Tests\Scanning\MissingFileTests.cs
git commit -m @'
feat(core): mark videos missing when a scan cannot find them

Scan tentatively flags every video under a source missing, then
clears the flag for each file it re-finds; restored files clear
automatically on the next scan. Files are never removed from the index.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 7: Core — browse read-model records + section/series summary queries

The UI browses Source → Section → Series/Standalone → Episode with unwatched counts and a thumbnail seed (first episode's file). We add three read-model records and three queries.

**Files:**
- Create: `src\VideoShelf.Core\Models\BrowseModels.cs`
- Modify: `src\VideoShelf.Core\Storage\LibraryRepository.cs`
- Create: `tests\VideoShelf.Core.Tests\Storage\BrowseQueryTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.Core.Tests\Storage\BrowseQueryTests.cs`:

```csharp
using System.IO;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class BrowseQueryTests
{
    private static (LibraryRepository lib, WatchRepository watch, long sectionId) Seed(TempDb temp, TempDir dir)
    {
        // Creator A: a 2-episode series + a standalone. Home Videos: 1 standalone.
        dir.Touch("Creator A/Cool Story.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");
        dir.Touch("Creator A/One Off.mp4");
        dir.Touch("Home Videos/Trip.mkv");

        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "My Videos");

        var sourceId = lib.GetSources().Single().Id;
        var sectionId = lib.GetSections(sourceId)
            .Single(s => s.FolderName == "Creator A").Id;
        return (lib, watch, sectionId);
    }

    [Fact]
    public void GetSectionSummaries_returns_unwatched_aggregate_per_section()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var (lib, _, _) = Seed(temp, dir);

        var sections = lib.GetSectionSummaries().OrderBy(s => s.DisplayName).ToList();

        sections.Select(s => s.DisplayName).ShouldBe(new[] { "Creator A", "Home Videos" });
        // Creator A has 3 videos, all unwatched.
        sections.Single(s => s.DisplayName == "Creator A").UnwatchedCount.ShouldBe(3);
        sections.Single(s => s.DisplayName == "Home Videos").UnwatchedCount.ShouldBe(1);
    }

    [Fact]
    public void GetSeriesSummaries_carries_standalone_flag_unwatched_and_thumb_seed()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var (lib, watch, sectionId) = Seed(temp, dir);

        var coolStoryVideos = lib.GetSeriesSummaries(sectionId);
        var cool = coolStoryVideos.Single(s => s.BaseTitle == "Cool Story");
        cool.IsStandalone.ShouldBeFalse();
        cool.EpisodeCount.ShouldBe(2);
        cool.UnwatchedCount.ShouldBe(2);
        cool.ThumbnailSeedPath.ShouldEndWith("Cool Story.mp4"); // first episode

        var oneOff = coolStoryVideos.Single(s => s.BaseTitle == "One Off");
        oneOff.IsStandalone.ShouldBeTrue();

        // Mark episode 1 watched: unwatched count drops to 1.
        var ep1 = lib.GetEpisodes(cool.SeriesId).First();
        watch.SetWatched(ep1.VideoId, true);
        lib.GetSeriesSummaries(sectionId).Single(s => s.BaseTitle == "Cool Story")
            .UnwatchedCount.ShouldBe(1);
    }

    [Fact]
    public void GetEpisodes_returns_rows_with_watched_and_missing()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var (lib, _, sectionId) = Seed(temp, dir);
        var cool = lib.GetSeriesSummaries(sectionId).Single(s => s.BaseTitle == "Cool Story");

        var eps = lib.GetEpisodes(cool.SeriesId);

        eps.Count.ShouldBe(2);
        eps.Select(e => e.EpisodeNo).ShouldBe(new[] { 1, 2 });
        eps.All(e => !e.Watched).ShouldBeTrue();
        eps.All(e => !e.Missing).ShouldBeTrue();
        eps.First().Title.ShouldBe("Cool Story");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.Core.Tests\VideoShelf.Core.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — the browse records and queries do not exist (compile error).

- [ ] **Step 3: Create the read-model records**

`src\VideoShelf.Core\Models\BrowseModels.cs`:

```csharp
namespace VideoShelf.Core.Models;

/// <summary>A section as shown in the browse sidebar, with its aggregated unwatched count.</summary>
public sealed record SectionSummary(
    long SectionId, long SourceId, string DisplayName, int SeriesCount, int UnwatchedCount);

/// <summary>A series or standalone card: episode/unwatched counts plus a thumbnail seed (first episode path).</summary>
public sealed record SeriesSummary(
    long SeriesId, long SectionId, string BaseTitle, bool IsStandalone,
    int EpisodeCount, int UnwatchedCount, string? ThumbnailSeedPath);

/// <summary>An episode row: identity, ordering, display title, and watched/missing flags.</summary>
public sealed record EpisodeView(
    long VideoId, long SeriesId, string FilePath, int EpisodeNo, string Title,
    bool Watched, bool Missing);
```

> `EpisodeView.Title` reuses the series `base_title` for episode 1 and `"<base_title> <episode_no>"` for later episodes — a simple, filename-independent display label.

- [ ] **Step 4: Add the queries to `LibraryRepository`**

Add `using VideoShelf.Core.Models;` is already present. Append these methods inside the class:

```csharp
    public IReadOnlyList<SectionSummary> GetSectionSummaries()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT sc.id, sc.source_id, sc.display_name,
                   COUNT(DISTINCT se.id) AS series_count,
                   COALESCE(SUM(CASE WHEN v.id IS NOT NULL AND v.watched = 0 THEN 1 ELSE 0 END), 0) AS unwatched
            FROM sections sc
            LEFT JOIN series se ON se.section_id = sc.id
            LEFT JOIN videos v ON v.series_id = se.id
            GROUP BY sc.id, sc.source_id, sc.display_name
            ORDER BY sc.display_name
            """;
        var list = new List<SectionSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SectionSummary(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4)));
        return list;
    }

    public IReadOnlyList<SeriesSummary> GetSeriesSummaries(long sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT se.id, se.section_id, se.base_title, se.is_standalone,
                   COUNT(v.id) AS episode_count,
                   COALESCE(SUM(CASE WHEN v.watched = 0 THEN 1 ELSE 0 END), 0) AS unwatched,
                   (SELECT file_path FROM videos vv WHERE vv.series_id = se.id
                    ORDER BY vv.episode_no LIMIT 1) AS thumb_seed
            FROM series se
            LEFT JOIN videos v ON v.series_id = se.id
            WHERE se.section_id = $sec
            GROUP BY se.id, se.section_id, se.base_title, se.is_standalone
            ORDER BY se.sort_key
            """;
        cmd.Parameters.AddWithValue("$sec", sectionId);
        var list = new List<SeriesSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SeriesSummary(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3) != 0,
                r.GetInt32(4), r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }

    public IReadOnlyList<EpisodeView> GetEpisodes(long seriesId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, v.series_id, v.file_path, v.episode_no, se.base_title, v.watched, v.missing
            FROM videos v
            JOIN series se ON se.id = v.series_id
            WHERE v.series_id = $s
            ORDER BY v.episode_no
            """;
        cmd.Parameters.AddWithValue("$s", seriesId);
        var list = new List<EpisodeView>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var episodeNo = r.GetInt32(3);
            var baseTitle = r.GetString(4);
            var title = episodeNo <= 1 ? baseTitle : $"{baseTitle} {episodeNo}";
            list.Add(new EpisodeView(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), episodeNo, title,
                r.GetInt64(5) != 0, r.GetInt64(6) != 0));
        }
        return list;
    }
```

- [ ] **Step 5: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **43 passing tests** (40 Core + 3 App).

- [ ] **Step 6: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.Core\Models\BrowseModels.cs src\VideoShelf.Core\Storage\LibraryRepository.cs tests\VideoShelf.Core.Tests\Storage\BrowseQueryTests.cs
git commit -m @'
feat(core): browse read-model + section/series/episode queries

SectionSummary/SeriesSummary/EpisodeView records and the aggregate
queries that build the browse tree with unwatched counts, standalone
flag, thumbnail seed (first episode), and watched/missing per episode.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 8: Core — title search (#1) across sections, series, videos

A single incremental search returns matching sections, series, and videos by title via indexed `LIKE`. We return a flat result list the VM can group.

**Files:**
- Modify: `src\VideoShelf.Core\Models\BrowseModels.cs`
- Modify: `src\VideoShelf.Core\Storage\LibraryRepository.cs`
- Create: `tests\VideoShelf.Core.Tests\Storage\SearchTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.Core.Tests\Storage\SearchTests.cs`:

```csharp
using System.Linq;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class SearchTests
{
    private static LibraryRepository Seed(TempDb temp, TempDir dir)
    {
        dir.Touch("Creator A/Cool Story.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");
        dir.Touch("Travel Vlogs/Iceland Trip.mkv");
        var lib = new LibraryRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "My Videos");
        return lib;
    }

    [Fact]
    public void Search_matches_section_series_and_video_titles_case_insensitively()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var lib = Seed(temp, dir);

        var results = lib.Search("cool");

        results.Any(r => r.Kind == SearchHitKind.Series && r.Title == "Cool Story").ShouldBeTrue();
        results.All(r => r.Title.Contains("Cool", System.StringComparison.OrdinalIgnoreCase)
                         || r.Kind == SearchHitKind.Video).ShouldBeTrue();
    }

    [Fact]
    public void Search_matches_section_name()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var lib = Seed(temp, dir);

        var results = lib.Search("travel");

        results.ShouldContain(r => r.Kind == SearchHitKind.Section && r.Title == "Travel Vlogs");
    }

    [Fact]
    public void Search_blank_query_returns_empty()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        var lib = Seed(temp, dir);

        lib.Search("   ").ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.Core.Tests\VideoShelf.Core.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `SearchHit`/`SearchHitKind`/`Search` do not exist (compile error).

- [ ] **Step 3: Add the search record + enum**

Append to `src\VideoShelf.Core\Models\BrowseModels.cs`:

```csharp
public enum SearchHitKind { Section, Series, Video }

/// <summary>One search result. TargetId is the section/series/video id matching Kind; SectionId
/// is the owning section (for jump-to-library navigation). For sections, SectionId == TargetId.</summary>
public sealed record SearchHit(SearchHitKind Kind, long TargetId, long SectionId, string Title);
```

- [ ] **Step 4: Add the `Search` query**

Append to `src\VideoShelf.Core\Storage\LibraryRepository.cs` (inside the class):

```csharp
    public IReadOnlyList<SearchHit> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        // Escape LIKE wildcards in user input; match anywhere (contains).
        var escaped = query.Trim()
            .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var pattern = "%" + escaped + "%";

        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 0 AS kind, sc.id AS target, sc.id AS section_id, sc.display_name AS title
            FROM sections sc WHERE sc.display_name LIKE $q ESCAPE '\'
            UNION ALL
            SELECT 1, se.id, se.section_id, se.base_title
            FROM series se WHERE se.base_title LIKE $q ESCAPE '\'
            UNION ALL
            SELECT 2, v.id, se.section_id, v.raw_filename
            FROM videos v JOIN series se ON se.id = v.series_id
            WHERE v.raw_filename LIKE $q ESCAPE '\'
            ORDER BY kind, title
            LIMIT 200
            """;
        cmd.Parameters.AddWithValue("$q", pattern);
        var list = new List<SearchHit>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SearchHit(
                (SearchHitKind)r.GetInt32(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3)));
        return list;
    }
```

> `display_name`, `base_title`, and `raw_filename` are all TEXT; SQLite `LIKE` is case-insensitive for ASCII by default, satisfying the case-insensitive requirement. The `ESCAPE '\'` clause makes the wildcard-escaping above effective.

- [ ] **Step 5: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **46 passing tests** (43 Core + 3 App).

- [ ] **Step 6: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.Core\Models\BrowseModels.cs src\VideoShelf.Core\Storage\LibraryRepository.cs tests\VideoShelf.Core.Tests\Storage\SearchTests.cs
git commit -m @'
feat(core): incremental title search across sections/series/videos

LIKE-backed UNION query returns SearchHits (Section/Series/Video)
with wildcard-escaped, case-insensitive matching and the owning
section id for jump-to-library navigation.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 9: Core — sort modes (name / date-added / recently-watched)

Section summaries and series summaries can be ordered three ways. We add a `BrowseSort` enum and overloads.

**Files:**
- Modify: `src\VideoShelf.Core\Models\BrowseModels.cs`
- Modify: `src\VideoShelf.Core\Storage\LibraryRepository.cs`
- Create: `tests\VideoShelf.Core.Tests\Storage\SortTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.Core.Tests\Storage\SortTests.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Threading;
using Shouldly;
using VideoShelf.Core.Models;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class SortTests
{
    [Fact]
    public void GetSeriesSummaries_sorts_by_name()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Sec/Banana.mp4");
        dir.Touch("Sec/Apple.mp4");
        var lib = new LibraryRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        var sectionId = lib.GetSectionSummaries().Single().SectionId;

        var byName = lib.GetSeriesSummaries(sectionId, BrowseSort.Name);

        byName.Select(s => s.BaseTitle).ShouldBe(new[] { "Apple", "Banana" });
    }

    [Fact]
    public void GetSeriesSummaries_sorts_by_recently_watched_first()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Sec/Apple.mp4");
        dir.Touch("Sec/Banana.mp4");
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        var sectionId = lib.GetSectionSummaries().Single().SectionId;

        // Watch Banana's episode -> Banana should sort first under RecentlyWatched.
        var banana = lib.GetSeriesSummaries(sectionId).Single(s => s.BaseTitle == "Banana");
        var bananaEp = lib.GetEpisodes(banana.SeriesId).First();
        watch.SetWatched(bananaEp.VideoId, true);

        var byWatched = lib.GetSeriesSummaries(sectionId, BrowseSort.RecentlyWatched);

        byWatched.First().BaseTitle.ShouldBe("Banana");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.Core.Tests\VideoShelf.Core.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `BrowseSort` and the `GetSeriesSummaries(long, BrowseSort)` overload do not exist (compile error).

- [ ] **Step 3: Add the `BrowseSort` enum**

Append to `src\VideoShelf.Core\Models\BrowseModels.cs`:

```csharp
public enum BrowseSort { Name, DateAdded, RecentlyWatched }
```

- [ ] **Step 4: Add the sort overload; keep the old method delegating to Name**

In `src\VideoShelf.Core\Storage\LibraryRepository.cs`, replace the existing `GetSeriesSummaries(long sectionId)` with an overload pair:

```csharp
    public IReadOnlyList<SeriesSummary> GetSeriesSummaries(long sectionId)
        => GetSeriesSummaries(sectionId, BrowseSort.Name);

    public IReadOnlyList<SeriesSummary> GetSeriesSummaries(long sectionId, BrowseSort sort)
    {
        var orderBy = sort switch
        {
            BrowseSort.DateAdded =>
                "(SELECT MAX(added_at) FROM videos vv WHERE vv.series_id = se.id) DESC, se.sort_key",
            BrowseSort.RecentlyWatched =>
                "(SELECT MAX(we.watched_at) FROM watch_events we " +
                "JOIN videos vv ON vv.id = we.video_id WHERE vv.series_id = se.id) DESC, se.sort_key",
            _ => "se.sort_key",
        };

        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT se.id, se.section_id, se.base_title, se.is_standalone,
                   COUNT(v.id) AS episode_count,
                   COALESCE(SUM(CASE WHEN v.watched = 0 THEN 1 ELSE 0 END), 0) AS unwatched,
                   (SELECT file_path FROM videos vv WHERE vv.series_id = se.id
                    ORDER BY vv.episode_no LIMIT 1) AS thumb_seed
            FROM series se
            LEFT JOIN videos v ON v.series_id = se.id
            WHERE se.section_id = $sec
            GROUP BY se.id, se.section_id, se.base_title, se.is_standalone
            ORDER BY {orderBy}
            """;
        cmd.Parameters.AddWithValue("$sec", sectionId);
        var list = new List<SeriesSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SeriesSummary(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3) != 0,
                r.GetInt32(4), r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }
```

> `orderBy` is a closed set of literal strings chosen by a `switch` on an enum — no user input reaches the SQL, so the interpolation is injection-safe. `RecentlyWatched` with no events sorts those series last (NULL `MAX` → `DESC` puts NULLs last in SQLite), then by `sort_key`.

- [ ] **Step 5: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **48 passing tests** (45 Core + 3 App). The Task 7 test using the no-arg overload still passes (delegates to `Name`).

- [ ] **Step 6: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.Core\Models\BrowseModels.cs src\VideoShelf.Core\Storage\LibraryRepository.cs tests\VideoShelf.Core.Tests\Storage\SortTests.cs
git commit -m @'
feat(core): browse sort by name / date-added / recently-watched

BrowseSort enum + GetSeriesSummaries overload; date-added uses
videos.added_at, recently-watched uses watch_events. Injection-safe
(ORDER BY chosen from a closed literal set).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 10: App — `IFolderPicker` abstraction + Win32 impl

A testable folder-picker interface; the concrete impl wraps WPF's `OpenFolderDialog`. The fake lets us test source management headlessly.

**Files:**
- Create: `src\VideoShelf.App\Services\IFolderPicker.cs`
- Create: `src\VideoShelf.App\Services\FolderPicker.cs`
- Modify: `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`
- Create: `tests\VideoShelf.App.Tests\TestSupport\FakeFolderPicker.cs`

- [ ] **Step 1: Write the interface**

`src\VideoShelf.App\Services\IFolderPicker.cs`:

```csharp
namespace VideoShelf.App.Services;

/// <summary>Abstracts the OS folder-chooser so source management is testable without UI.</summary>
public interface IFolderPicker
{
    /// <summary>Returns the chosen folder's full path, or null if the user cancelled.</summary>
    string? PickFolder(string? initialFolder = null);
}
```

- [ ] **Step 2: Write the Win32 impl**

`src\VideoShelf.App\Services\FolderPicker.cs`:

```csharp
using Microsoft.Win32;

namespace VideoShelf.App.Services;

public sealed class FolderPicker : IFolderPicker
{
    public string? PickFolder(string? initialFolder = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Add a video source folder",
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialFolder))
            dialog.InitialDirectory = initialFolder;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
```

- [ ] **Step 3: Write the test fake**

`tests\VideoShelf.App.Tests\TestSupport\FakeFolderPicker.cs`:

```csharp
using System.Collections.Generic;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests.TestSupport;

/// <summary>Returns queued folders in order; null when exhausted (simulating cancel).</summary>
public sealed class FakeFolderPicker : IFolderPicker
{
    private readonly Queue<string?> _queued;

    public FakeFolderPicker(params string?[] folders) => _queued = new Queue<string?>(folders);

    public string? PickFolder(string? initialFolder = null)
        => _queued.Count > 0 ? _queued.Dequeue() : null;
}
```

- [ ] **Step 4: Register the concrete picker in DI**

In `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`, add inside `AddVideoShelf` (before the viewmodel registrations):

```csharp
        services.AddSingleton<IFolderPicker, FolderPicker>();
```

- [ ] **Step 5: Verify the build (no behavior test yet — exercised in Task 11)**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet build VideoShelf.slnx -c Release --nologo
```

Expected: `Build succeeded`. (No new unit test in this task; the fake is consumed by Task 11's `SourcesViewModel` tests.)

- [ ] **Step 6: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\Services\IFolderPicker.cs src\VideoShelf.App\Services\FolderPicker.cs src\VideoShelf.App\Services\ServiceCollectionExtensions.cs tests\VideoShelf.App.Tests\TestSupport\FakeFolderPicker.cs
git commit -m @'
feat(app): IFolderPicker abstraction + Win32 OpenFolderDialog impl

Testable folder-chooser seam with a queue-backed fake for headless
source-management tests.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 11: App — `SourcesViewModel` (add / remove / list)

**Files:**
- Create: `src\VideoShelf.App\ViewModels\SourcesViewModel.cs`
- Modify: `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`
- Modify: `src\VideoShelf.Core\Storage\LibraryRepository.cs` (add `RemoveSource`)
- Create: `tests\VideoShelf.App.Tests\SourcesViewModelTests.cs`
- Create: `tests\VideoShelf.App.Tests\TestSupport\AppTempDb.cs`

- [ ] **Step 1: Write a shared temp-db helper for App tests**

`tests\VideoShelf.App.Tests\TestSupport\AppTempDb.cs`:

```csharp
using System;
using System.IO;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests.TestSupport;

/// <summary>A migrated VideoShelfDb backed by a temp file, deleted on Dispose.</summary>
public sealed class AppTempDb : IDisposable
{
    public string DbPath { get; }
    public VideoShelfDb Db { get; }

    public AppTempDb()
    {
        DbPath = Path.Combine(Path.GetTempPath(), "vshelf_app_db_" + Guid.NewGuid().ToString("N") + ".db");
        Db = new VideoShelfDb(DbPath);
        Db.Migrate();
    }

    public void Dispose()
    {
        Db.Dispose();
        try { File.Delete(DbPath); } catch { }
        try { File.Delete(DbPath + "-wal"); } catch { }
        try { File.Delete(DbPath + "-shm"); } catch { }
    }
}
```

- [ ] **Step 2: Write the failing test**

`tests\VideoShelf.App.Tests\SourcesViewModelTests.cs`:

```csharp
using System.Linq;
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class SourcesViewModelTests
{
    [Fact]
    public void AddSource_picks_a_folder_and_persists_it()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var picker = new FakeFolderPicker(@"C:\Videos\RootA");
        var vm = new SourcesViewModel(lib, picker);
        vm.Load();

        vm.AddSourceCommand.Execute(null);

        vm.Sources.Select(s => s.RootPath).ShouldBe(new[] { @"C:\Videos\RootA" });
        lib.GetSources().Single().RootPath.ShouldBe(@"C:\Videos\RootA");
    }

    [Fact]
    public void AddSource_cancelled_picker_adds_nothing()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var vm = new SourcesViewModel(lib, new FakeFolderPicker((string?)null));
        vm.Load();

        vm.AddSourceCommand.Execute(null);

        vm.Sources.ShouldBeEmpty();
    }

    [Fact]
    public void RemoveSource_deletes_the_selected_source()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var vm = new SourcesViewModel(lib, new FakeFolderPicker(@"C:\Videos\RootA"));
        vm.Load();
        vm.AddSourceCommand.Execute(null);
        var added = vm.Sources.Single();

        vm.RemoveSourceCommand.Execute(added);

        vm.Sources.ShouldBeEmpty();
        lib.GetSources().ShouldBeEmpty();
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `SourcesViewModel` and `LibraryRepository.RemoveSource` do not exist (compile error).

- [ ] **Step 4: Add `RemoveSource` to Core**

Append to `src\VideoShelf.Core\Storage\LibraryRepository.cs` (inside the class):

```csharp
    public void RemoveSource(long sourceId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        // ON DELETE CASCADE removes the source's sections/series/videos; foreign_keys=ON is set in Open().
        cmd.CommandText = "DELETE FROM sources WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", sourceId);
        cmd.ExecuteNonQuery();
    }
```

- [ ] **Step 5: Implement `SourcesViewModel`**

`src\VideoShelf.App\ViewModels\SourcesViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SourcesViewModel(LibraryRepository library, IFolderPicker picker)
    : ObservableObject
{
    public ObservableCollection<Source> Sources { get; } = [];

    public void Load()
    {
        Sources.Clear();
        foreach (var s in library.GetSources())
            Sources.Add(s);
    }

    [RelayCommand]
    private void AddSource()
    {
        var folder = picker.PickFolder();
        if (string.IsNullOrWhiteSpace(folder))
            return;

        var displayName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))
            is { Length: > 0 } name ? name : folder;
        library.UpsertSource(folder, displayName);
        Load();
    }

    [RelayCommand]
    private void RemoveSource(Source? source)
    {
        if (source is null)
            return;
        library.RemoveSource(source.Id);
        Load();
    }
}
```

- [ ] **Step 6: Register `SourcesViewModel` in DI**

In `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`, add before `MainViewModel`:

```csharp
        services.AddSingleton<SourcesViewModel>();
```

- [ ] **Step 7: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **51 passing tests** (46 Core + 5 App).

- [ ] **Step 8: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\ViewModels\SourcesViewModel.cs src\VideoShelf.App\Services\ServiceCollectionExtensions.cs src\VideoShelf.Core\Storage\LibraryRepository.cs tests\VideoShelf.App.Tests\SourcesViewModelTests.cs tests\VideoShelf.App.Tests\TestSupport\AppTempDb.cs
git commit -m @'
feat(app): SourcesViewModel add/remove/list + Core RemoveSource

Add picks a folder (via IFolderPicker), derives a display name, and
upserts; remove deletes the source (cascading its sections/series/
videos). Cancelled picker is a no-op.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 12: App — `IScanCoordinator` + background scan-all

A coordinator that scans every source on a background thread, idempotently, reporting busy state. Tested synchronously by awaiting the returned task.

**Files:**
- Create: `src\VideoShelf.App\Services\IScanCoordinator.cs`
- Create: `src\VideoShelf.App\Services\ScanCoordinator.cs`
- Modify: `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`
- Create: `tests\VideoShelf.App.Tests\ScanCoordinatorTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.App.Tests\ScanCoordinatorTests.cs`:

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class ScanCoordinatorTests
{
    [Fact]
    public async Task ScanAll_indexes_every_source()
    {
        using var temp = new AppTempDb();
        using var dirA = new TempDir();
        using var dirB = new TempDir();
        dirA.Touch("Creator A/Cool Story.mp4");
        dirB.Touch("Vlogs/Trip.mkv");

        var lib = new LibraryRepository(temp.Db);
        lib.UpsertSource(dirA.Path, "A");
        lib.UpsertSource(dirB.Path, "B");

        var scan = new ScanService(temp.Db, lib);
        var coordinator = new ScanCoordinator(lib, scan);

        await coordinator.ScanAllAsync(CancellationToken.None);

        lib.GetSectionSummaries().Select(s => s.DisplayName).OrderBy(n => n)
            .ShouldBe(new[] { "Creator A", "Vlogs" });
    }

    [Fact]
    public async Task ScanAll_reports_not_busy_after_completion()
    {
        using var temp = new AppTempDb();
        var lib = new LibraryRepository(temp.Db);
        var coordinator = new ScanCoordinator(lib, new ScanService(temp.Db, lib));

        await coordinator.ScanAllAsync(CancellationToken.None);

        coordinator.IsBusy.ShouldBeFalse();
    }
}
```

> This test references `VideoShelf.Core.Tests.TestSupport.TempDir`. Add a project reference so the App tests can reuse it (Step 2).

- [ ] **Step 2: Let App.Tests reference Core.Tests' `TempDir`**

In `tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj`, add to the `<ItemGroup>` that holds project references:

```xml
    <ProjectReference Include="..\VideoShelf.Core.Tests\VideoShelf.Core.Tests.csproj" />
```

> Reusing the existing `TempDir` keeps the fixtures DRY. `TempDir`/`TempDb` are `public` in Core.Tests, so they resolve fine.

- [ ] **Step 3: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `IScanCoordinator`/`ScanCoordinator` do not exist (compile error).

- [ ] **Step 4: Write the interface**

`src\VideoShelf.App\Services\IScanCoordinator.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace VideoShelf.App.Services;

public interface IScanCoordinator
{
    bool IsBusy { get; }

    /// <summary>Scans every registered source on a background thread. Idempotent and crash-safe.</summary>
    Task ScanAllAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Implement `ScanCoordinator`**

`src\VideoShelf.App\Services\ScanCoordinator.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Services;

public sealed class ScanCoordinator(LibraryRepository library, ScanService scanService) : IScanCoordinator
{
    private volatile bool _busy;

    public bool IsBusy => _busy;

    public async Task ScanAllAsync(CancellationToken cancellationToken)
    {
        _busy = true;
        try
        {
            await Task.Run(() =>
            {
                foreach (var source in library.GetSources())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    scanService.ScanSource(source.RootPath, source.DisplayName);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _busy = false;
        }
    }
}
```

- [ ] **Step 6: Register in DI**

In `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`, add `using VideoShelf.Core.Scanning;` at the top and, inside `AddVideoShelf` before the viewmodel registrations:

```csharp
        services.AddSingleton<ScanService>();
        services.AddSingleton<IScanCoordinator, ScanCoordinator>();
```

> `ScanService`'s ctor is `(VideoShelfDb db, LibraryRepository library)` — both already registered — so DI resolves it.

- [ ] **Step 7: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **53 passing tests** (46 Core + 7 App).

- [ ] **Step 8: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\Services\IScanCoordinator.cs src\VideoShelf.App\Services\ScanCoordinator.cs src\VideoShelf.App\Services\ServiceCollectionExtensions.cs tests\VideoShelf.App.Tests\ScanCoordinatorTests.cs tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj
git commit -m @'
feat(app): ScanCoordinator scans all sources on a background thread

Idempotent scan-all with busy state + cancellation; App.Tests reuses
Core.Tests TempDir fixture.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 13: App — `IThumbnailService` + testable `ThumbnailCache`

The cache/path/fallback logic is unit-tested with a fake snapshotter; the concrete libVLC service stays thin (Task 14).

**Files:**
- Create: `src\VideoShelf.App\Services\IThumbnailService.cs`
- Create: `src\VideoShelf.App\Services\ThumbnailCache.cs`
- Create: `tests\VideoShelf.App.Tests\ThumbnailCacheTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.App.Tests\ThumbnailCacheTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;

namespace VideoShelf.App.Tests;

public class ThumbnailCacheTests
{
    private sealed class FakeSnapshotter : IThumbnailSnapshotter
    {
        private readonly bool _succeed;
        public int Calls { get; private set; }
        public FakeSnapshotter(bool succeed) => _succeed = succeed;

        public Task<bool> TrySnapshotAsync(string videoPath, string outputPngPath, CancellationToken ct)
        {
            Calls++;
            if (_succeed)
                File.WriteAllBytes(outputPngPath, new byte[] { 1, 2, 3 });
            return Task.FromResult(_succeed);
        }
    }

    private static string TempThumbDir()
        => Path.Combine(Path.GetTempPath(), "vshelf_thumbs_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetThumbnail_creates_then_reuses_cached_png()
    {
        var dir = TempThumbDir();
        try
        {
            var snap = new FakeSnapshotter(succeed: true);
            var cache = new ThumbnailCache(dir, snap);

            var first = await cache.GetThumbnailPathAsync(@"C:\V\S\a.mp4", CancellationToken.None);
            var second = await cache.GetThumbnailPathAsync(@"C:\V\S\a.mp4", CancellationToken.None);

            first.ShouldNotBeNull();
            File.Exists(first!).ShouldBeTrue();
            second.ShouldBe(first);
            snap.Calls.ShouldBe(1); // second call served from cache
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task GetThumbnail_returns_null_when_snapshot_fails_and_never_throws()
    {
        var dir = TempThumbDir();
        try
        {
            var cache = new ThumbnailCache(dir, new FakeSnapshotter(succeed: false));

            var result = await cache.GetThumbnailPathAsync(@"C:\V\S\missing.mp4", CancellationToken.None);

            result.ShouldBeNull();
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void CacheKey_is_stable_and_path_dependent()
    {
        var a1 = ThumbnailCache.CacheFileName(@"C:\V\S\a.mp4");
        var a2 = ThumbnailCache.CacheFileName(@"C:\V\S\a.mp4");
        var b = ThumbnailCache.CacheFileName(@"C:\V\S\b.mp4");

        a1.ShouldBe(a2);
        a1.ShouldNotBe(b);
        a1.ShouldEndWith(".png");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `IThumbnailService`/`IThumbnailSnapshotter`/`ThumbnailCache` do not exist (compile error).

- [ ] **Step 3: Write the interfaces**

`src\VideoShelf.App\Services\IThumbnailService.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace VideoShelf.App.Services;

/// <summary>Returns a disk path to a cached poster thumbnail for a video, or null if unavailable.</summary>
public interface IThumbnailService
{
    Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken cancellationToken);
}

/// <summary>Low-level frame grab. Implementations write a PNG to outputPngPath and return true on success.
/// Must NOT throw — return false on any failure so the cache can fall back to a placeholder.</summary>
public interface IThumbnailSnapshotter
{
    Task<bool> TrySnapshotAsync(string videoPath, string outputPngPath, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement `ThumbnailCache`**

`src\VideoShelf.App\Services\ThumbnailCache.cs`:

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VideoShelf.App.Services;

/// <summary>
/// Caches poster thumbnails as PNGs under a directory, keyed by a hash of the video's full path.
/// Fail-safe: any snapshot failure yields null (a placeholder), never an exception into the UI.
/// </summary>
public sealed class ThumbnailCache(string cacheDirectory, IThumbnailSnapshotter snapshotter) : IThumbnailService
{
    public static string CacheFileName(string videoPath)
    {
        var bytes = Encoding.UTF8.GetBytes(videoPath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash) + ".png";
    }

    public async Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            var target = Path.Combine(cacheDirectory, CacheFileName(videoPath));

            if (File.Exists(target) && new FileInfo(target).Length > 0)
                return target;

            // Snapshot to a temp file, then move into place — a crash mid-write never leaves a
            // corrupt cache entry (defensive: place-then-rename).
            var temp = target + ".tmp";
            var ok = await snapshotter.TrySnapshotAsync(videoPath, temp, cancellationToken)
                .ConfigureAwait(false);

            if (!ok || !File.Exists(temp) || new FileInfo(temp).Length == 0)
            {
                TryDelete(temp);
                return null;
            }

            File.Move(temp, target, overwrite: true);
            return target;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null; // fail-safe: never throw a thumbnail error into the UI
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **56 passing tests** (46 Core + 10 App).

- [ ] **Step 6: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\Services\IThumbnailService.cs src\VideoShelf.App\Services\ThumbnailCache.cs tests\VideoShelf.App.Tests\ThumbnailCacheTests.cs
git commit -m @'
feat(app): IThumbnailService + disk-cached ThumbnailCache (fail-safe)

Path-hash cache key, place-then-rename writes, cache reuse, and
null-on-failure fallback (never throws into the UI). Snapshotting is
behind IThumbnailSnapshotter so the cache is unit-tested with a fake.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 14: App — libVLC NuGet refs + thin `LibVlcThumbnailService`

Introduce LibVLCSharp + VideoLAN.LibVLC.Windows in **Core** (per spec §3, self-contained). The concrete snapshotter is thin and not unit-tested here (the Phase 6 harness exercises it with generated clips); it must satisfy the fail-safe contract (return false, never throw).

**Files:**
- Modify: `src\VideoShelf.Core\VideoShelf.Core.csproj`
- Create: `src\VideoShelf.App\Services\LibVlcThumbnailService.cs`
- Modify: `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`

- [ ] **Step 1: Add the libVLC packages to Core**

In `src\VideoShelf.Core\VideoShelf.Core.csproj`, add to the package `<ItemGroup>`:

```xml
    <PackageReference Include="LibVLCSharp" Version="3.9.4" />
    <PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.0.21" />
```

> These are the same major versions used across the .NET WPF + libVLC ecosystem; `VideoLAN.LibVLC.Windows` bundles native libVLC so nothing is required on PATH (honors the self-contained principle). If restore reports a newer compatible patch, pin to the latest 3.x that restores cleanly and note it in the PR.

- [ ] **Step 2: Verify restore/build with the new packages**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet build VideoShelf.slnx -c Release --nologo
```

Expected: `Build succeeded`. (No code uses libVLC yet; this step proves the packages restore on the build agent before we depend on them.)

- [ ] **Step 3: Implement the thin snapshotter**

`src\VideoShelf.App\Services\LibVlcThumbnailService.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace VideoShelf.App.Services;

/// <summary>
/// Grabs a single representative frame via a headless libVLC MediaPlayer snapshot.
/// Thin by design: real coverage comes from the Phase 6 harness with generated clips.
/// Honors the fail-safe contract — returns false on any error, never throws.
/// </summary>
public sealed class LibVlcThumbnailService : IThumbnailSnapshotter, IDisposable
{
    private readonly LibVLC _libVlc;

    public LibVlcThumbnailService()
    {
        Core.Initialize(); // LibVLCSharp.Shared.Core — loads bundled native libVLC
        _libVlc = new LibVLC("--no-audio", "--no-video-title-show", "--quiet");
    }

    public async Task<bool> TrySnapshotAsync(string videoPath, string outputPngPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(videoPath))
                return false;

            using var media = new Media(_libVlc, new Uri(videoPath));
            using var player = new MediaPlayer(media) { Mute = true };

            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnPlaying(object? s, EventArgs e) => ready.TrySetResult(true);
            player.Playing += OnPlaying;

            if (!player.Play())
                return false;

            using (cancellationToken.Register(() => ready.TrySetResult(false)))
            {
                var startedTask = await Task.WhenAny(ready.Task, Task.Delay(5000, cancellationToken))
                    .ConfigureAwait(false);
                if (startedTask != ready.Task || !ready.Task.Result)
                {
                    player.Stop();
                    return false;
                }
            }

            // Seek a little in so we don't capture a black leader frame, then snapshot.
            if (player.Length > 0)
                player.Time = Math.Min(player.Length / 10, 3000);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);

            var taken = player.TakeSnapshot(0, outputPngPath, 0, 0);
            player.Stop();

            return taken && File.Exists(outputPngPath) && new FileInfo(outputPngPath).Length > 0;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false; // fail-safe
        }
    }

    public void Dispose() => _libVlc.Dispose();
}
```

> `LibVLCSharp.Shared.Core.Initialize()` and `TakeSnapshot` are the documented libVLC snapshot path. `width=0,height=0` keeps the source aspect/size. The 5 s play timeout and 300 ms settle keep a stuck file from hanging the cache; on any failure we return false so `ThumbnailCache` falls back to a placeholder.

- [ ] **Step 4: Register the real services in DI**

In `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`, add to `AddVideoShelf` (before viewmodels). The thumbnail directory comes from `AppPaths`:

```csharp
        services.AddSingleton<IThumbnailSnapshotter, LibVlcThumbnailService>();
        services.AddSingleton<IThumbnailService>(sp =>
            new ThumbnailCache(
                sp.GetRequiredService<AppPaths>().ThumbnailDirectory,
                sp.GetRequiredService<IThumbnailSnapshotter>()));
```

- [ ] **Step 5: Verify the build (no new unit test — thin service)**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **56 passing tests** (unchanged count; the libVLC service has no unit test by design). Build must succeed with the new package references.

- [ ] **Step 6: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.Core\VideoShelf.Core.csproj src\VideoShelf.App\Services\LibVlcThumbnailService.cs src\VideoShelf.App\Services\ServiceCollectionExtensions.cs
git commit -m @'
feat(app): bundle libVLC + thin LibVlcThumbnailService snapshotter

LibVLCSharp + VideoLAN.LibVLC.Windows (self-contained, no PATH tools).
Headless MediaPlayer snapshot behind IThumbnailSnapshotter; fail-safe
(returns false, never throws). Wired into the ThumbnailCache via DI.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 15: App — `EpisodeViewModel` (watched toggle + missing flag)

**Files:**
- Create: `src\VideoShelf.App\ViewModels\EpisodeViewModel.cs`
- Create: `tests\VideoShelf.App.Tests\EpisodeViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.App.Tests\EpisodeViewModelTests.cs`:

```csharp
using Shouldly;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests;

public class EpisodeViewModelTests
{
    private static (WatchRepository watch, long videoId) Seed(AppTempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        var videoId = lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
        return (new WatchRepository(temp.Db), videoId);
    }

    [Fact]
    public void ToggleWatched_flips_flag_and_persists()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = Seed(temp);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", Watched: false, Missing: false);
        var vm = new EpisodeViewModel(view, watch);

        vm.ToggleWatchedCommand.Execute(null);

        vm.Watched.ShouldBeTrue();
        watch.IsWatched(videoId).ShouldBeTrue();

        vm.ToggleWatchedCommand.Execute(null);
        vm.Watched.ShouldBeFalse();
        watch.IsWatched(videoId).ShouldBeFalse();
    }

    [Fact]
    public void Missing_episode_exposes_flag_for_dimming()
    {
        using var temp = new AppTempDb();
        var (watch, videoId) = Seed(temp);
        var view = new EpisodeView(videoId, 1, @"C:\V\S\a.mp4", 1, "Base", Watched: false, Missing: true);

        var vm = new EpisodeViewModel(view, watch);

        vm.IsMissing.ShouldBeTrue();
        vm.Title.ShouldBe("Base");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `EpisodeViewModel` does not exist (compile error).

- [ ] **Step 3: Implement `EpisodeViewModel`**

`src\VideoShelf.App\ViewModels\EpisodeViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class EpisodeViewModel(EpisodeView model, WatchRepository watch) : ObservableObject
{
    public long VideoId => model.VideoId;
    public string Title => model.Title;
    public int EpisodeNo => model.EpisodeNo;
    public string FilePath => model.FilePath;
    public bool IsMissing => model.Missing;

    [ObservableProperty]
    private bool _watched = model.Watched;

    [RelayCommand]
    private void ToggleWatched()
    {
        Watched = !Watched;
        watch.SetWatched(model.VideoId, Watched);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **58 passing tests** (46 Core + 12 App).

- [ ] **Step 5: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\ViewModels\EpisodeViewModel.cs tests\VideoShelf.App.Tests\EpisodeViewModelTests.cs
git commit -m @'
feat(app): EpisodeViewModel with watched toggle + missing flag

Persists watched state via WatchRepository; exposes IsMissing for
dimming and a display Title from the read-model.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 16: App — `SeriesViewModel` (badge + thumbnail + lazy episodes)

**Files:**
- Create: `src\VideoShelf.App\ViewModels\SeriesViewModel.cs`
- Create: `tests\VideoShelf.App.Tests\SeriesViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.App.Tests\SeriesViewModelTests.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class SeriesViewModelTests
{
    private sealed class StubThumbnailService : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(@"C:\thumbs\seed.png");
    }

    private static (LibraryRepository lib, WatchRepository watch, long sectionId) Seed(AppTempDb temp, TempDir dir)
    {
        dir.Touch("Sec/Cool Story.mp4");
        dir.Touch("Sec/Cool Story 2.mp4");
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        var sectionId = lib.GetSectionSummaries().Single().SectionId;
        return (lib, watch, sectionId);
    }

    [Fact]
    public void UnwatchedBadge_shows_count_and_hides_when_fully_watched()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (lib, watch, sectionId) = Seed(temp, dir);
        var summary = lib.GetSeriesSummaries(sectionId).Single();
        var vm = new SeriesViewModel(summary, lib, watch, new StubThumbnailService());

        vm.UnwatchedCount.ShouldBe(2);
        vm.HasUnwatched.ShouldBeTrue();

        // Watch both episodes, refresh.
        foreach (var e in lib.GetEpisodes(summary.SeriesId))
            watch.SetWatched(e.VideoId, true);
        vm.Refresh();

        vm.UnwatchedCount.ShouldBe(0);
        vm.HasUnwatched.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadEpisodes_populates_child_viewmodels_in_order()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (lib, watch, sectionId) = Seed(temp, dir);
        var summary = lib.GetSeriesSummaries(sectionId).Single();
        var vm = new SeriesViewModel(summary, lib, watch, new StubThumbnailService());

        await vm.LoadEpisodesAsync(CancellationToken.None);

        vm.Episodes.Select(e => e.EpisodeNo).ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task LoadThumbnail_sets_path_from_service()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (lib, watch, sectionId) = Seed(temp, dir);
        var summary = lib.GetSeriesSummaries(sectionId).Single();
        var vm = new SeriesViewModel(summary, lib, watch, new StubThumbnailService());

        await vm.LoadThumbnailAsync(CancellationToken.None);

        vm.ThumbnailPath.ShouldBe(@"C:\thumbs\seed.png");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `SeriesViewModel` does not exist (compile error).

- [ ] **Step 3: Implement `SeriesViewModel`**

`src\VideoShelf.App\ViewModels\SeriesViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SeriesViewModel(
    SeriesSummary summary,
    LibraryRepository library,
    WatchRepository watch,
    IThumbnailService thumbnails) : ObservableObject
{
    public long SeriesId => summary.SeriesId;
    public string BaseTitle => summary.BaseTitle;
    public bool IsStandalone => summary.IsStandalone;
    public int EpisodeCount => summary.EpisodeCount;

    public ObservableCollection<EpisodeViewModel> Episodes { get; } = [];

    [ObservableProperty]
    private int _unwatchedCount = summary.UnwatchedCount;

    [ObservableProperty]
    private string? _thumbnailPath;

    public bool HasUnwatched => UnwatchedCount > 0;

    partial void OnUnwatchedCountChanged(int value) => OnPropertyChanged(nameof(HasUnwatched));

    /// <summary>Recomputes the unwatched badge from the DB (after a watched toggle).</summary>
    public void Refresh()
    {
        var fresh = 0;
        foreach (var e in library.GetEpisodes(summary.SeriesId))
            if (!e.Watched) fresh++;
        UnwatchedCount = fresh;
    }

    public async Task LoadEpisodesAsync(CancellationToken cancellationToken)
    {
        var rows = await Task.Run(() => library.GetEpisodes(summary.SeriesId), cancellationToken)
            .ConfigureAwait(false);
        Episodes.Clear();
        foreach (var row in rows)
            Episodes.Add(new EpisodeViewModel(row, watch));
    }

    public async Task LoadThumbnailAsync(CancellationToken cancellationToken)
    {
        if (summary.ThumbnailSeedPath is null)
            return;
        ThumbnailPath = await thumbnails.GetThumbnailPathAsync(summary.ThumbnailSeedPath, cancellationToken)
            .ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **61 passing tests** (46 Core + 15 App).

- [ ] **Step 5: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\ViewModels\SeriesViewModel.cs tests\VideoShelf.App.Tests\SeriesViewModelTests.cs
git commit -m @'
feat(app): SeriesViewModel — unwatched badge, lazy episodes, thumbnail

HasUnwatched/UnwatchedCount badge (hidden at 0), lazy episode load
into child VMs, and async thumbnail path from IThumbnailService.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 17: App — `SectionViewModel` (aggregate badge + lazy series)

**Files:**
- Create: `src\VideoShelf.App\ViewModels\SectionViewModel.cs`
- Create: `tests\VideoShelf.App.Tests\SectionViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.App.Tests\SectionViewModelTests.cs`:

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class SectionViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task LoadSeries_populates_children_with_chosen_sort()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        dir.Touch("Sec/Banana.mp4");
        dir.Touch("Sec/Apple.mp4");
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        var summary = lib.GetSectionSummaries().Single();
        var vm = new SectionViewModel(summary, lib, watch, new NullThumbs());

        await vm.LoadSeriesAsync(BrowseSort.Name, CancellationToken.None);

        vm.SeriesList.Select(s => s.BaseTitle).ShouldBe(new[] { "Apple", "Banana" });
        vm.DisplayName.ShouldBe("Sec");
        vm.UnwatchedCount.ShouldBe(2);
        vm.HasUnwatched.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `SectionViewModel` does not exist (compile error).

- [ ] **Step 3: Implement `SectionViewModel`**

`src\VideoShelf.App\ViewModels\SectionViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class SectionViewModel(
    SectionSummary summary,
    LibraryRepository library,
    WatchRepository watch,
    IThumbnailService thumbnails) : ObservableObject
{
    public long SectionId => summary.SectionId;
    public string DisplayName => summary.DisplayName;

    public ObservableCollection<SeriesViewModel> SeriesList { get; } = [];

    [ObservableProperty]
    private int _unwatchedCount = summary.UnwatchedCount;

    public bool HasUnwatched => UnwatchedCount > 0;

    partial void OnUnwatchedCountChanged(int value) => OnPropertyChanged(nameof(HasUnwatched));

    public async Task LoadSeriesAsync(BrowseSort sort, CancellationToken cancellationToken)
    {
        var summaries = await Task.Run(
            () => library.GetSeriesSummaries(summary.SectionId, sort), cancellationToken)
            .ConfigureAwait(false);

        SeriesList.Clear();
        var unwatched = 0;
        foreach (var s in summaries)
        {
            SeriesList.Add(new SeriesViewModel(s, library, watch, thumbnails));
            unwatched += s.UnwatchedCount;
        }
        UnwatchedCount = unwatched;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **62 passing tests** (46 Core + 16 App).

- [ ] **Step 5: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\ViewModels\SectionViewModel.cs tests\VideoShelf.App.Tests\SectionViewModelTests.cs
git commit -m @'
feat(app): SectionViewModel — aggregate unwatched badge + lazy series

Lazy series load honoring the chosen BrowseSort; aggregates child
unwatched counts into the section badge (hidden at 0).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 18: App — `LibraryViewModel` (sections, sort selection, search)

The root browse VM: loads sections, holds the sort selection (reloading the open section), and runs incremental search.

**Files:**
- Create: `src\VideoShelf.App\ViewModels\LibraryViewModel.cs`
- Modify: `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`
- Create: `tests\VideoShelf.App.Tests\LibraryViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.App.Tests\LibraryViewModelTests.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using System.Threading;

namespace VideoShelf.App.Tests;

public class LibraryViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private static LibraryViewModel Build(AppTempDb temp, TempDir dir)
    {
        dir.Touch("Creator A/Cool Story.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");
        dir.Touch("Travel Vlogs/Iceland.mkv");
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        return new LibraryViewModel(lib, watch, new NullThumbs());
    }

    [Fact]
    public async Task LoadSections_lists_all_sections_sorted_by_name()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var vm = Build(temp, dir);

        await vm.LoadSectionsAsync();

        vm.Sections.Select(s => s.DisplayName).ShouldBe(new[] { "Creator A", "Travel Vlogs" });
    }

    [Fact]
    public async Task SelectingSection_loads_its_series()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var vm = Build(temp, dir);
        await vm.LoadSectionsAsync();

        await vm.SelectSectionAsync(vm.Sections.Single(s => s.DisplayName == "Creator A"));

        vm.SelectedSection!.SeriesList.Single().BaseTitle.ShouldBe("Cool Story");
    }

    [Fact]
    public async Task ChangingSort_reloads_open_section()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var vm = Build(temp, dir);
        await vm.LoadSectionsAsync();
        await vm.SelectSectionAsync(vm.Sections.First());

        vm.SortMode = BrowseSort.DateAdded; // triggers reload

        // allow the async reload kicked off by the setter to complete
        await vm.WaitForIdleAsync();
        vm.SelectedSection!.SeriesList.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Search_populates_results_and_clear_empties_them()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var vm = Build(temp, dir);
        await vm.LoadSectionsAsync();

        vm.SearchText = "iceland";
        await vm.WaitForIdleAsync();
        vm.SearchResults.ShouldContain(h => h.Title == "Travel Vlogs" || h.Title.Contains("Iceland"));

        vm.SearchText = "";
        await vm.WaitForIdleAsync();
        vm.SearchResults.ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `LibraryViewModel` does not exist (compile error).

- [ ] **Step 3: Implement `LibraryViewModel`**

`src\VideoShelf.App\ViewModels\LibraryViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;
using VideoShelf.Core.Models;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

public sealed partial class LibraryViewModel(
    LibraryRepository library,
    WatchRepository watch,
    IThumbnailService thumbnails) : ObservableObject
{
    private Task _pending = Task.CompletedTask;

    public ObservableCollection<SectionViewModel> Sections { get; } = [];
    public ObservableCollection<SearchHit> SearchResults { get; } = [];

    [ObservableProperty]
    private SectionViewModel? _selectedSection;

    [ObservableProperty]
    private BrowseSort _sortMode = BrowseSort.Name;

    [ObservableProperty]
    private string _searchText = "";

    public async Task LoadSectionsAsync()
    {
        var summaries = await Task.Run(library.GetSectionSummaries).ConfigureAwait(false);
        Sections.Clear();
        foreach (var s in summaries)
            Sections.Add(new SectionViewModel(s, library, watch, thumbnails));
    }

    public async Task SelectSectionAsync(SectionViewModel? section)
    {
        SelectedSection = section;
        if (section is not null)
            await section.LoadSeriesAsync(SortMode, CancellationToken.None).ConfigureAwait(false);
    }

    partial void OnSortModeChanged(BrowseSort value)
    {
        if (SelectedSection is { } section)
            _pending = section.LoadSeriesAsync(value, CancellationToken.None);
    }

    partial void OnSearchTextChanged(string value)
    {
        _pending = RunSearchAsync(value);
    }

    private async Task RunSearchAsync(string query)
    {
        var hits = await Task.Run(() => library.Search(query)).ConfigureAwait(false);
        SearchResults.Clear();
        foreach (var h in hits)
            SearchResults.Add(h);
    }

    /// <summary>Test/affordance hook: awaits the most recently started async reload/search.</summary>
    public Task WaitForIdleAsync() => _pending;

    [RelayCommand]
    private async Task Refresh() => await LoadSectionsAsync();
}
```

> Setters kick off async work and stash the task in `_pending` so tests (and a future "busy" affordance) can await it deterministically. `ObservableCollection` mutations here run on the test thread; in the app, the VM methods are invoked from the dispatcher.

- [ ] **Step 4: Register in DI**

In `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`, add before `MainViewModel`:

```csharp
        services.AddSingleton<LibraryViewModel>();
```

- [ ] **Step 5: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **66 passing tests** (46 Core + 20 App).

- [ ] **Step 6: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\ViewModels\LibraryViewModel.cs src\VideoShelf.App\Services\ServiceCollectionExtensions.cs tests\VideoShelf.App.Tests\LibraryViewModelTests.cs
git commit -m @'
feat(app): LibraryViewModel — sections, sort selection, search

Loads sections, selects/loads a section's series, reloads on sort
change, and runs incremental search into SearchResults. WaitForIdle
exposes the pending async reload for deterministic tests.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 19: App — compose `MainViewModel` (sources + library + scan)

Wire the shell VM to expose the sources, library, scan command, and an initial-load that bootstraps everything.

**Files:**
- Modify: `src\VideoShelf.App\ViewModels\MainViewModel.cs`
- Modify: `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`
- Create: `tests\VideoShelf.App.Tests\MainViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.App.Tests\MainViewModelTests.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;
using System.Threading;

namespace VideoShelf.App.Tests;

public class MainViewModelTests
{
    private sealed class NullThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task Scan_then_initialize_populates_sources_and_library()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Cool Story.mp4");

        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var scanService = new ScanService(temp.Db, lib);
        var coordinator = new ScanCoordinator(lib, scanService);

        var sources = new SourcesViewModel(lib, new FakeFolderPicker(dir.Path));
        var libraryVm = new LibraryViewModel(lib, watch, new NullThumbs());
        var vm = new MainViewModel(sources, libraryVm, coordinator);

        // Add a source via the sources VM, then scan + reload through the shell.
        sources.Load();
        sources.AddSourceCommand.Execute(null);
        await vm.ScanAndReloadCommand.ExecuteAsync(null);

        vm.Sources.Sources.Single().RootPath.ShouldBe(dir.Path);
        vm.Library.Sections.Single().DisplayName.ShouldBe("Creator A");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `MainViewModel` has no `Sources`/`Library`/`ScanAndReloadCommand` (compile error).

- [ ] **Step 3: Implement the composed `MainViewModel`**

Replace `src\VideoShelf.App\ViewModels\MainViewModel.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.App.Services;

namespace VideoShelf.App.ViewModels;

public sealed partial class MainViewModel(
    SourcesViewModel sources,
    LibraryViewModel library,
    IScanCoordinator scanCoordinator) : ObservableObject
{
    public string Title => "VideoShelf";

    public SourcesViewModel Sources => sources;
    public LibraryViewModel Library => library;

    [ObservableProperty]
    private bool _isScanning;

    /// <summary>Loads sources + library once at startup.</summary>
    public async Task InitializeAsync()
    {
        Sources.Load();
        await Library.LoadSectionsAsync();
    }

    [RelayCommand]
    private async Task ScanAndReload()
    {
        IsScanning = true;
        try
        {
            await scanCoordinator.ScanAllAsync(CancellationToken.None);
            Sources.Load();
            await Library.LoadSectionsAsync();
        }
        finally
        {
            IsScanning = false;
        }
    }
}
```

> `Title` is now a get-only property (no longer `[ObservableProperty]`). The Task 1 host-builds test asserts `vm.Title == "VideoShelf"`, which still holds.

- [ ] **Step 4: Update DI registration for the new ctor**

In `src\VideoShelf.App\Services\ServiceCollectionExtensions.cs`, the `MainViewModel` registration stays `services.AddSingleton<MainViewModel>();` — DI resolves `SourcesViewModel`, `LibraryViewModel`, and `IScanCoordinator` automatically. Confirm those three are registered earlier in `AddVideoShelf` (they are, from Tasks 11, 12, 18). No change needed unless the build complains.

- [ ] **Step 5: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **67 passing tests** (46 Core + 21 App). The Task 1 `HostBuildsTests` still passes (`Title` is still `"VideoShelf"`).

- [ ] **Step 6: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\ViewModels\MainViewModel.cs tests\VideoShelf.App.Tests\MainViewModelTests.cs
git commit -m @'
feat(app): compose MainViewModel (sources + library + scan)

Exposes the SourcesViewModel + LibraryViewModel, an InitializeAsync
startup load, and a ScanAndReload command that runs the coordinator
then refreshes sources + sections.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 20: App — shell views (`MainWindow.xaml` browse UI)

XAML-only task: build the FluentWindow shell with a sources sidebar, the section/series/episode browse area, a search box, a sort selector, watched toggles, and thumbnail images. No unit test — verify by `dotnet build` and a written eyeball checklist. **Theming rule:** never re-base a WPF-UI control's Style/Template for cosmetics — additive (Opacity/RenderTransform/margins) only.

**Files:**
- Modify: `src\VideoShelf.App\Views\MainWindow.xaml`
- Modify: `src\VideoShelf.App\Views\MainWindow.xaml.cs`

- [ ] **Step 1: Replace `MainWindow.xaml` with the browse shell**

`src\VideoShelf.App\Views\MainWindow.xaml`:

```xml
<ui:FluentWindow x:Class="VideoShelf.App.Views.MainWindow"
                 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
                 xmlns:vm="clr-namespace:VideoShelf.App.ViewModels"
                 Title="VideoShelf"
                 Width="1180"
                 Height="760"
                 MinWidth="900"
                 MinHeight="600"
                 ExtendsContentIntoTitleBar="True"
                 WindowBackdropType="Mica"
                 WindowStartupLocation="CenterScreen">
    <ui:FluentWindow.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- Tokens merged per-view so the window parses self-contained. -->
                <ResourceDictionary Source="/VideoShelf.App;component/Resources/DesignTokens.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </ui:FluentWindow.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="44" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" Title="VideoShelf" />

        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="288" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <!-- LEFT: sources + sections sidebar -->
            <Border Grid.Column="0" Padding="16"
                    BorderBrush="{StaticResource DividerBrush}"
                    BorderThickness="0,0,1,0">
                <DockPanel>
                    <TextBlock DockPanel.Dock="Top" Text="SOURCES" Style="{StaticResource SectionHeader}" />
                    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,8,0,8">
                        <ui:Button Content="Add source" Command="{Binding Sources.AddSourceCommand}" />
                        <ui:Button Content="Scan" Margin="8,0,0,0"
                                   Command="{Binding ScanAndReloadCommand}" />
                    </StackPanel>
                    <ItemsControl DockPanel.Dock="Top" ItemsSource="{Binding Sources.Sources}" Margin="0,0,0,8">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <DockPanel Margin="0,2">
                                    <ui:Button DockPanel.Dock="Right" Content="Remove"
                                               Command="{Binding DataContext.Sources.RemoveSourceCommand,
                                                         RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                               CommandParameter="{Binding}" />
                                    <TextBlock Text="{Binding DisplayName}" VerticalAlignment="Center"
                                               Opacity="0.85" />
                                </DockPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <TextBlock DockPanel.Dock="Top" Margin="{StaticResource SectionGap}"
                               Text="SECTIONS" Style="{StaticResource SectionHeader}" />
                    <ListBox ItemsSource="{Binding Library.Sections}"
                             SelectedItem="{Binding Library.SelectedSection, Mode=TwoWay}"
                             VirtualizingStackPanel.IsVirtualizing="True"
                             VirtualizingStackPanel.VirtualizationMode="Recycling"
                             BorderThickness="0" Background="Transparent">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <DockPanel>
                                    <Border DockPanel.Dock="Right" Background="{StaticResource AccentBrush}"
                                            CornerRadius="{StaticResource ControlRadius}"
                                            Padding="6,1" VerticalAlignment="Center"
                                            Visibility="{Binding HasUnwatched,
                                                Converter={StaticResource BoolToVisibility}}">
                                        <TextBlock Text="{Binding UnwatchedCount}" FontSize="11"
                                                   Foreground="#101010" />
                                    </Border>
                                    <TextBlock Text="{Binding DisplayName}" VerticalAlignment="Center" />
                                </DockPanel>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </DockPanel>
            </Border>

            <!-- RIGHT: search + sort + series/episodes -->
            <DockPanel Grid.Column="1" Margin="16">
                <Grid DockPanel.Dock="Top" Margin="0,0,0,12">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <ui:TextBox Grid.Column="0" PlaceholderText="Search…"
                                Text="{Binding Library.SearchText, UpdateSourceTrigger=PropertyChanged}" />
                    <ComboBox Grid.Column="1" Width="170" Margin="8,0,0,0"
                              SelectedIndex="{Binding Library.SortMode, Converter={StaticResource SortModeToIndex}}">
                        <ComboBoxItem Content="Name" />
                        <ComboBoxItem Content="Date added" />
                        <ComboBoxItem Content="Recently watched" />
                    </ComboBox>
                </Grid>

                <!-- Search results overlay the browse list when present -->
                <ItemsControl DockPanel.Dock="Top" ItemsSource="{Binding Library.SearchResults}"
                              Margin="0,0,0,8">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Margin="0,2" Opacity="0.85">
                                <Run Text="{Binding Kind, Mode=OneWay}" />
                                <Run Text=" · " />
                                <Run Text="{Binding Title, Mode=OneWay}" />
                            </TextBlock>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <!-- Series cards for the selected section -->
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <ItemsControl ItemsSource="{Binding Library.SelectedSection.SeriesList}"
                                  VirtualizingStackPanel.IsVirtualizing="True"
                                  VirtualizingStackPanel.VirtualizationMode="Recycling">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Margin="0,0,0,10" Padding="10"
                                        Background="{StaticResource SubtleFillBrush}"
                                        CornerRadius="{StaticResource CardRadius}">
                                    <DockPanel>
                                        <Border DockPanel.Dock="Left" Width="120" Height="68"
                                                Background="{StaticResource ThumbPlaceholderBrush}"
                                                CornerRadius="{StaticResource ControlRadius}"
                                                Margin="0,0,12,0">
                                            <Image Style="{StaticResource ThumbnailImage}"
                                                   Source="{Binding ThumbnailPath, IsAsync=True,
                                                            NotifyOnTargetUpdated=True}" />
                                        </Border>
                                        <StackPanel>
                                            <DockPanel>
                                                <Border DockPanel.Dock="Right" Background="{StaticResource AccentBrush}"
                                                        CornerRadius="{StaticResource ControlRadius}"
                                                        Padding="6,1"
                                                        Visibility="{Binding HasUnwatched,
                                                            Converter={StaticResource BoolToVisibility}}">
                                                    <TextBlock FontSize="11" Foreground="#101010">
                                                        <Run Text="{Binding UnwatchedCount, Mode=OneWay}" />
                                                        <Run Text=" unwatched" />
                                                    </TextBlock>
                                                </Border>
                                                <TextBlock Text="{Binding BaseTitle}" FontWeight="SemiBold" />
                                            </DockPanel>
                                            <ItemsControl ItemsSource="{Binding Episodes}" Margin="0,6,0,0">
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate>
                                                        <DockPanel Margin="0,2"
                                                                   Opacity="{Binding IsMissing,
                                                                       Converter={StaticResource MissingToOpacity}}">
                                                            <CheckBox DockPanel.Dock="Right"
                                                                      IsChecked="{Binding Watched, Mode=OneWay}"
                                                                      Command="{Binding ToggleWatchedCommand}" />
                                                            <TextBlock Text="{Binding Title}" />
                                                        </DockPanel>
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                            </ItemsControl>
                                        </StackPanel>
                                    </DockPanel>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </DockPanel>
        </Grid>
    </Grid>
</ui:FluentWindow>
```

> The series-card template binds `Episodes` and `ThumbnailPath`, which are populated lazily. Task 21 adds the per-section "load series/thumbnails/episodes" trigger wired to section selection; for this XAML task the bindings simply render empty until that lands. The three converters referenced (`BoolToVisibility`, `MissingToOpacity`, `SortModeToIndex`) are created in Step 2.

- [ ] **Step 2: Create the value converters**

`src\VideoShelf.App\Converters\Converters.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VideoShelf.Core.Models;

namespace VideoShelf.App.Converters;

public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

public sealed class MissingToOpacity : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? 0.45 : 1.0;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

public sealed class SortModeToIndex : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is BrowseSort s ? (int)s : 0;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => value is int i ? (BrowseSort)i : BrowseSort.Name;
}
```

Register the converters in `MainWindow.xaml`'s resource dictionary — add inside `<ResourceDictionary>` after the merged dictionaries:

```xml
            <conv:BoolToVisibility x:Key="BoolToVisibility" />
            <conv:MissingToOpacity x:Key="MissingToOpacity" />
            <conv:SortModeToIndex x:Key="SortModeToIndex" />
```

…and add the namespace to the `FluentWindow` opening tag:

```xml
                 xmlns:conv="clr-namespace:VideoShelf.App.Converters"
```

- [ ] **Step 3: Set the DataContext and trigger initial load in code-behind**

`src\VideoShelf.App\Views\MainWindow.xaml.cs`:

```csharp
using System;
using Wpf.Ui.Controls;
using VideoShelf.App.ViewModels;

namespace VideoShelf.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            try { await _viewModel.InitializeAsync(); }
            catch { /* startup load is best-effort; surfaced via empty UI */ }
        };
    }
}
```

- [ ] **Step 4: Verify the build (XAML has no unit test)**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet build VideoShelf.slnx -c Release --nologo
```

Expected: `Build succeeded`. Eyeball checklist (to confirm when the app is launched in a later harness phase):
- Sidebar shows SOURCES (Add/Scan buttons + list with Remove) and SECTIONS (list with unwatched pills).
- Right pane shows a Search box + a Sort combo (Name / Date added / Recently watched).
- Selecting a section shows series cards with a thumbnail placeholder, an "N unwatched" pill, and episode rows with a watched checkbox.
- Missing episodes render dimmed (opacity ~0.45).
- No WPF-UI control had its Style/Template re-based — only additive Opacity/margins were used.

- [ ] **Step 5: Run the full suite (unchanged count) + commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **67 passing tests** (unchanged; no new unit tests for XAML).

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\Views\MainWindow.xaml src\VideoShelf.App\Views\MainWindow.xaml.cs src\VideoShelf.App\Converters\Converters.cs
git commit -m @'
feat(app): browse shell UI (sources sidebar + series/episode cards)

FluentWindow with sources sidebar, section list with unwatched pills,
search box, sort selector, series cards (thumbnail + unwatched pill +
episode rows with watched checkbox), and dimmed missing episodes.
Additive theming only (no WPF-UI Style/Template re-base).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 21: App — wire section selection to lazy load (series → thumbnails → episodes) + watched-refresh

When a section is selected, its series load (per current sort), each series' thumbnail + episodes load, and toggling an episode watched updates the badges. We extend `LibraryViewModel.SelectSectionAsync` to fan out and add a refresh path.

**Files:**
- Modify: `src\VideoShelf.App\ViewModels\LibraryViewModel.cs`
- Modify: `src\VideoShelf.App\ViewModels\SectionViewModel.cs`
- Modify: `src\VideoShelf.App\ViewModels\EpisodeViewModel.cs`
- Create: `tests\VideoShelf.App.Tests\BrowseFanoutTests.cs`

- [ ] **Step 1: Write the failing test**

`tests\VideoShelf.App.Tests\BrowseFanoutTests.cs`:

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.Tests.TestSupport;
using VideoShelf.App.ViewModels;
using VideoShelf.Core.Models;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.App.Tests;

public class BrowseFanoutTests
{
    private sealed class SeedThumbs : IThumbnailService
    {
        public Task<string?> GetThumbnailPathAsync(string videoPath, CancellationToken ct)
            => Task.FromResult<string?>(@"C:\thumbs\x.png");
    }

    private static (LibraryViewModel vm, LibraryRepository lib) Build(AppTempDb temp, TempDir dir)
    {
        dir.Touch("Sec/Cool Story.mp4");
        dir.Touch("Sec/Cool Story 2.mp4");
        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        new ScanService(temp.Db, lib).ScanSource(dir.Path, "V");
        return (new LibraryViewModel(lib, watch, new SeedThumbs()), lib);
    }

    [Fact]
    public async Task SelectingSection_loads_series_with_episodes_and_thumbnails()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (vm, _) = Build(temp, dir);
        await vm.LoadSectionsAsync();

        await vm.SelectSectionAsync(vm.Sections.Single());

        var series = vm.SelectedSection!.SeriesList.Single();
        series.Episodes.Select(e => e.EpisodeNo).ShouldBe(new[] { 1, 2 });
        series.ThumbnailPath.ShouldBe(@"C:\thumbs\x.png");
    }

    [Fact]
    public async Task TogglingEpisode_watched_updates_series_and_section_badges()
    {
        using var temp = new AppTempDb();
        using var dir = new TempDir();
        var (vm, _) = Build(temp, dir);
        await vm.LoadSectionsAsync();
        await vm.SelectSectionAsync(vm.Sections.Single());
        var section = vm.SelectedSection!;
        var series = section.SeriesList.Single();

        series.UnwatchedCount.ShouldBe(2);

        series.Episodes.First().ToggleWatchedCommand.Execute(null);

        series.UnwatchedCount.ShouldBe(1);
        section.UnwatchedCount.ShouldBe(1);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test tests\VideoShelf.App.Tests\VideoShelf.App.Tests.csproj -c Release --nologo -v q
```

Expected: FAIL — `SelectSectionAsync` does not load episodes/thumbnails, and toggling does not refresh badges.

- [ ] **Step 3: Fan out the section load in `SectionViewModel`**

In `src\VideoShelf.App\ViewModels\SectionViewModel.cs`, replace `LoadSeriesAsync` so it also loads each series' episodes + thumbnail, and add a `RecomputeUnwatched` helper that re-sums children:

```csharp
    public async Task LoadSeriesAsync(BrowseSort sort, CancellationToken cancellationToken)
    {
        var summaries = await Task.Run(
            () => library.GetSeriesSummaries(summary.SectionId, sort), cancellationToken)
            .ConfigureAwait(false);

        SeriesList.Clear();
        foreach (var s in summaries)
        {
            var seriesVm = new SeriesViewModel(s, library, watch, thumbnails);
            seriesVm.UnwatchedChanged += (_, _) => RecomputeUnwatched();
            SeriesList.Add(seriesVm);
            await seriesVm.LoadEpisodesAsync(cancellationToken).ConfigureAwait(false);
            await seriesVm.LoadThumbnailAsync(cancellationToken).ConfigureAwait(false);
        }
        RecomputeUnwatched();
    }

    public void RecomputeUnwatched()
    {
        var total = 0;
        foreach (var s in SeriesList)
            total += s.UnwatchedCount;
        UnwatchedCount = total;
    }
```

- [ ] **Step 4: Raise an event from `SeriesViewModel` when its badge changes; refresh on child toggle**

In `src\VideoShelf.App\ViewModels\SeriesViewModel.cs`, add an event and wire episode toggles to refresh the series badge. Replace the `OnUnwatchedCountChanged` partial and add wiring in `LoadEpisodesAsync`:

```csharp
    public event System.EventHandler? UnwatchedChanged;

    partial void OnUnwatchedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnwatched));
        UnwatchedChanged?.Invoke(this, System.EventArgs.Empty);
    }

    public async Task LoadEpisodesAsync(CancellationToken cancellationToken)
    {
        var rows = await Task.Run(() => library.GetEpisodes(summary.SeriesId), cancellationToken)
            .ConfigureAwait(false);
        Episodes.Clear();
        foreach (var row in rows)
        {
            var ep = new EpisodeViewModel(row, watch);
            ep.WatchedChanged += (_, _) => Refresh();
            Episodes.Add(ep);
        }
    }
```

- [ ] **Step 5: Raise an event from `EpisodeViewModel` on toggle**

In `src\VideoShelf.App\ViewModels\EpisodeViewModel.cs`, add the event and fire it after persisting:

```csharp
    public event System.EventHandler? WatchedChanged;

    [RelayCommand]
    private void ToggleWatched()
    {
        Watched = !Watched;
        watch.SetWatched(model.VideoId, Watched);
        WatchedChanged?.Invoke(this, System.EventArgs.Empty);
    }
```

- [ ] **Step 6: Make `LibraryViewModel.SelectSectionAsync` simply delegate (already does)**

`SelectSectionAsync` already calls `section.LoadSeriesAsync(SortMode, …)`, which now fans out. No change needed. Confirm the method body still reads:

```csharp
    public async Task SelectSectionAsync(SectionViewModel? section)
    {
        SelectedSection = section;
        if (section is not null)
            await section.LoadSeriesAsync(SortMode, CancellationToken.None).ConfigureAwait(false);
    }
```

- [ ] **Step 7: Run the test to verify it passes**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: PASS — **69 passing tests** (46 Core + 23 App). The earlier `SeriesViewModelTests` / `SectionViewModelTests` still pass (the new event wiring is additive; `LoadSeriesAsync` still sorts as before).

- [ ] **Step 8: Commit**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
git add src\VideoShelf.App\ViewModels\LibraryViewModel.cs src\VideoShelf.App\ViewModels\SectionViewModel.cs src\VideoShelf.App\ViewModels\SeriesViewModel.cs src\VideoShelf.App\ViewModels\EpisodeViewModel.cs tests\VideoShelf.App.Tests\BrowseFanoutTests.cs
git commit -m @'
feat(app): section selection fans out to episodes + thumbnails; live badges

Selecting a section loads its series with episodes + thumbnails;
toggling an episode watched refreshes the series and section unwatched
badges via change events.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Task 22: Phase finalization — full suite + whole-branch review prep

**Files:** none (verification only)

- [ ] **Step 1: Run the full gate**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet test VideoShelf.slnx -c Release --nologo -v q
```

Expected: **69 passing tests, 0 failures** (46 Core + 23 App). If any fail, fix before finishing per the runbook.

- [ ] **Step 2: Confirm the release build is clean**

```powershell
cd "C:\Agent Projects\VideoShelf\.worktrees\feat-app-shell"
dotnet build VideoShelf.slnx -c Release --nologo
```

Expected: `Build succeeded`, 0 warnings introduced by this phase (libVLC native-asset copy messages are expected and acceptable).

- [ ] **Step 3: Eyeball the spec checklist (no code)**

Confirm each Phase 2 spec item maps to a task:
- §4 library model + multi-source scan → Tasks 11–12.
- §5 schema (`missing`, `added_at`, `resume_position` columns) → Task 4 (resume column added now, used in Phase 3).
- §6 search (#1) → Tasks 8, 18; sort (#3) → Tasks 9, 18, 20; unwatched badges (#6) → Tasks 7, 16, 17, 21; missing-file marking (#9) → Tasks 6, 15, 20; thumbnails → Tasks 13, 14, 16, 20.
- Read-only on files, idempotent crash-safe migration, self-contained (libVLC only) → Tasks 4, 6, 14.

There is no commit in this task (verification only); the phase is ready for the runbook's finish step (push → PR → CI → merge).

---

## Self-Review (completed by plan author)

**1. Spec coverage.** Every Phase 2 spec item is covered: §4 (multi-source library + scan: Tasks 11–12), §5 (schema columns `missing`/`added_at`/`resume_position`: Task 4; `added_at` stamping: Task 5), §6 search #1 (Tasks 8, 18), sort #3 (Tasks 9, 18, 20), unwatched badges #6 (Tasks 7, 16, 17, 21), missing-file marking #9 (Tasks 6, 15, 20), thumbnails (Tasks 13–14, 16, 20). Out-of-scope items (playback §9, discovery §7, tagging §8, rename §10) are explicitly excluded.

**2. Placeholder scan.** No "TBD"/"similar to above"/"add error handling" — every code step contains complete, runnable code. The libVLC service is intentionally thin (documented), not a placeholder.

**3. Type consistency.** Verified across tasks: `SeriesSummary.ThumbnailSeedPath`, `EpisodeView` field order `(VideoId, SeriesId, FilePath, EpisodeNo, Title, Watched, Missing)`, `BrowseSort { Name, DateAdded, RecentlyWatched }`, `IThumbnailService.GetThumbnailPathAsync`, `IThumbnailSnapshotter.TrySnapshotAsync`, `SeriesViewModel.UnwatchedChanged` / `EpisodeViewModel.WatchedChanged`, and the `LibraryRepository` method names (`GetSectionSummaries`, `GetSeriesSummaries`, `GetEpisodes`, `Search`, `MarkAllMissingForSource`, `ClearMissing`, `RemoveSource`) are used identically wherever referenced. `Video` record's new fields `AddedAt`/`Missing` are read in `GetVideosForSeries`. Test counts accumulate consistently (baseline 32 → 69).
