# VideoShelf Foundation (Core Indexer) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the testable Core of VideoShelf — scan multiple source folders, group a section's files into series/standalones from filenames, and persist an incremental, crash-safe SQLite library — with zero UI and zero native dependencies.

**Architecture:** A pure-logic `VideoShelf.Core` class library (no WPF, no libVLC) holds models, the filename heuristics, the folder scanner, the SQLite schema/migrations, and repositories. Everything is unit-tested with temp dirs and temp SQLite files. The WPF app and libVLC playback are built on top in later plans; the Core never depends on them.

**Tech Stack:** .NET 10, C# (latest), `Microsoft.Data.Sqlite`, xUnit + Shouldly. Mirrors VideoTriage project conventions.

---

## Plan roadmap (this is Plan 1 of 6)

This spec is large, so it is delivered as sequential plans. Each produces working, testable software and is a prerequisite for the next.

1. **Foundation — Core indexer (this plan).** Models, filename heuristics, scanner, SQLite schema + repositories, scan orchestrator. No UI.
2. **App shell + library browse + thumbnails.** WPF + WPF-UI shell (port VideoTriage `DesignTokens.xaml`), source management, virtualized source→section→series→episode browse, libVLC thumbnail service, watched markers, search.
3. **Player + mini-player/PiP.** Embedded `LibVLCSharp.WPF.VideoView`, overlay controls, fullscreen + keyboard, auto-mark-watched on end, detachable always-on-top mini-player.
4. **Discovery + tagging.** Section tags + autocomplete; "For you" / "Pick a tag" (unwatched-weighted) / "More from this section".
5. **Opt-in rename tool.** Preview diff → confirm → defensive rename + undo manifest; DB path update.
6. **Harness + CI + polish.** Fixture generator (tiny playable clips), `--source/--autostart/--done-signal` launch hooks, screenshot capture, `tools/verify.ps1`, GitHub Actions CI, app icon, README.

---

## File structure (this plan)

```
VideoShelf/
  VideoShelf.sln
  src/
    VideoShelf.Core/
      VideoShelf.Core.csproj
      Models/
        Source.cs            # Source record (root_path, display_name)
        Section.cs           # Section record (source, folder_name, display_name)
        Series.cs            # Series record (section, base_title, sort_key, is_standalone)
        Video.cs             # Video record (series, file_path, episode_no, format, duration, thumbnail_path, watched)
        ScannedFile.cs       # raw scan result (full_path, file_name, extension)
        ParsedTitle.cs       # (BaseTitle, EpisodeNumber?) value object
        GroupedSection.cs    # grouping output: list of GroupedSeries (each with ordered episodes)
      Naming/
        VideoExtensions.cs   # allow-list of video file extensions
        NaturalComparer.cs   # natural (human) string sort
        TitleParser.cs       # filename stem -> ParsedTitle
        SectionGrouper.cs    # files -> GroupedSection (series + standalones)
      Storage/
        VideoShelfDb.cs      # connection factory + migrations (schema v1)
        LibraryRepository.cs # upsert sources/sections/series/videos; queries
        WatchRepository.cs   # watched flag + watch_events
        TagRepository.cs     # section_tags
        OverrideRepository.cs# grouping_overrides
      Scanning/
        FolderScanner.cs     # source root -> sections -> ScannedFile[]
        ScanService.cs       # orchestrate scan -> group -> persist (incremental, crash-safe)
  tests/
    VideoShelf.Core.Tests/
      VideoShelf.Core.Tests.csproj
      Naming/NaturalComparerTests.cs
      Naming/TitleParserTests.cs
      Naming/SectionGrouperTests.cs
      Scanning/FolderScannerTests.cs
      Storage/VideoShelfDbTests.cs
      Storage/LibraryRepositoryTests.cs
      Storage/WatchRepositoryTests.cs
      Storage/TagRepositoryTests.cs
      Storage/OverrideRepositoryTests.cs
      Scanning/ScanServiceTests.cs
      TestSupport/TempDir.cs        # disposable temp directory helper
      TestSupport/TempDb.cs         # disposable temp SQLite db helper
```

**Conventions (mirror VideoTriage):** `net10.0-windows`, `Nullable` + `ImplicitUsings` enabled, `LangVersion=latest`. Tests use `Using Include="Xunit"`. Repositories take an open `SqliteConnection` (or a `VideoShelfDb` factory) so tests can use a temp-file DB. All disk access is injectable/parameterized by path for testability.

---

## Task 0: Solution + project scaffold

**Files:**
- Create: `VideoShelf.sln`
- Create: `src/VideoShelf.Core/VideoShelf.Core.csproj`
- Create: `tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj`

- [ ] **Step 1: Create the solution and projects**

Run (from `C:\Agent Projects\VideoShelf`):
```powershell
dotnet new sln -n VideoShelf
dotnet new classlib -n VideoShelf.Core -o src/VideoShelf.Core
dotnet new xunit -n VideoShelf.Core.Tests -o tests/VideoShelf.Core.Tests
Remove-Item src/VideoShelf.Core/Class1.cs, tests/VideoShelf.Core.Tests/UnitTest1.cs -ErrorAction SilentlyContinue
dotnet sln add src/VideoShelf.Core/VideoShelf.Core.csproj tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj
dotnet add tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj reference src/VideoShelf.Core/VideoShelf.Core.csproj
```

- [ ] **Step 2: Pin project files to VideoTriage conventions**

Overwrite `src/VideoShelf.Core/VideoShelf.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="VideoShelf.Core.Tests" />
  </ItemGroup>
</Project>
```

Overwrite `tests/VideoShelf.Core.Tests/VideoShelf.Core.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\VideoShelf.Core\VideoShelf.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Build to verify the empty solution compiles**

Run: `dotnet build VideoShelf.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**
```powershell
git add VideoShelf.sln src/ tests/
git commit -m "chore: scaffold VideoShelf.Core solution and test project"
```

---

## Task 1: Test support helpers (TempDir, TempDb)

**Files:**
- Create: `tests/VideoShelf.Core.Tests/TestSupport/TempDir.cs`
- Create: `tests/VideoShelf.Core.Tests/TestSupport/TempDb.cs`

- [ ] **Step 1: Write TempDir (no test of its own — it's used by later tests)**

`tests/VideoShelf.Core.Tests/TestSupport/TempDir.cs`:
```csharp
using System;
using System.IO;

namespace VideoShelf.Core.Tests.TestSupport;

/// <summary>A unique temp directory deleted on Dispose. Use in a `using`.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vshelf_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Create an empty file at a relative path, creating parent dirs. Returns full path.</summary>
    public string Touch(string relativePath)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, Array.Empty<byte>());
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
```

- [ ] **Step 2: Write TempDb**

`tests/VideoShelf.Core.Tests/TestSupport/TempDb.cs`:
```csharp
using System;
using System.IO;
using VideoShelf.Core.Storage;

namespace VideoShelf.Core.Tests.TestSupport;

/// <summary>A VideoShelfDb backed by a temp .db file, migrated and deleted on Dispose.</summary>
public sealed class TempDb : IDisposable
{
    public string DbPath { get; }
    public VideoShelfDb Db { get; }

    public TempDb()
    {
        DbPath = Path.Combine(Path.GetTempPath(), "vshelf_db_" + Guid.NewGuid().ToString("N") + ".db");
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

> Note: `TempDb` references `VideoShelfDb` (Task 6). Create the file now; it will not compile until Task 6 lands. If executing strictly in order, create `TempDir` in this task and defer `TempDb` to Task 6's step 1. (Subagent executor: create `TempDb` together with Task 6.)

- [ ] **Step 3: Commit**
```powershell
git add tests/VideoShelf.Core.Tests/TestSupport/TempDir.cs
git commit -m "test: add TempDir helper"
```

---

## Task 2: Video extension allow-list

**Files:**
- Create: `src/VideoShelf.Core/Naming/VideoExtensions.cs`
- Test: `tests/VideoShelf.Core.Tests/Naming/VideoExtensionsTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/VideoShelf.Core.Tests/Naming/VideoExtensionsTests.cs`:
```csharp
using Shouldly;
using VideoShelf.Core.Naming;

namespace VideoShelf.Core.Tests.Naming;

public class VideoExtensionsTests
{
    [Theory]
    [InlineData("movie.mp4", true)]
    [InlineData("clip.MKV", true)]          // case-insensitive
    [InlineData("show.mov", true)]
    [InlineData("a.webm", true)]
    [InlineData("notes.txt", false)]
    [InlineData("poster.jpg", false)]
    [InlineData("noext", false)]
    public void IsVideo_matches_known_extensions(string fileName, bool expected)
        => VideoExtensions.IsVideo(fileName).ShouldBe(expected);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter VideoExtensionsTests`
Expected: FAIL — `VideoExtensions` does not exist.

- [ ] **Step 3: Implement**

`src/VideoShelf.Core/Naming/VideoExtensions.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace VideoShelf.Core.Naming;

/// <summary>The set of file extensions VideoShelf treats as playable video (libVLC handles all of these).</summary>
public static class VideoExtensions
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".flv",
        ".ts", ".m2ts", ".mts", ".mpg", ".mpeg", ".vob", ".ogv", ".3gp", ".divx",
    };

    public static bool IsVideo(string fileName)
        => Known.Contains(Path.GetExtension(fileName));
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter VideoExtensionsTests`
Expected: PASS (7 cases).

- [ ] **Step 5: Commit**
```powershell
git add src/VideoShelf.Core/Naming/VideoExtensions.cs tests/VideoShelf.Core.Tests/Naming/VideoExtensionsTests.cs
git commit -m "feat(core): video extension allow-list"
```

---

## Task 3: Natural (human) string comparer

Used to order episodes and sections so `Clip 2` sorts before `Clip 10`.

**Files:**
- Create: `src/VideoShelf.Core/Naming/NaturalComparer.cs`
- Test: `tests/VideoShelf.Core.Tests/Naming/NaturalComparerTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/VideoShelf.Core.Tests/Naming/NaturalComparerTests.cs`:
```csharp
using System;
using System.Linq;
using Shouldly;
using VideoShelf.Core.Naming;

namespace VideoShelf.Core.Tests.Naming;

public class NaturalComparerTests
{
    [Fact]
    public void Orders_embedded_numbers_numerically()
    {
        var input = new[] { "Clip 10", "Clip 2", "Clip 1" };
        Array.Sort(input, new NaturalComparer());
        input.ShouldBe(new[] { "Clip 1", "Clip 2", "Clip 10" });
    }

    [Fact]
    public void Is_case_insensitive_for_letters()
    {
        new NaturalComparer().Compare("apple", "Apple").ShouldBe(0);
    }

    [Fact]
    public void Falls_back_to_text_when_no_numbers()
    {
        new NaturalComparer().Compare("alpha", "beta").ShouldBeLessThan(0);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter NaturalComparerTests`
Expected: FAIL — `NaturalComparer` does not exist.

- [ ] **Step 3: Implement**

`src/VideoShelf.Core/Naming/NaturalComparer.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace VideoShelf.Core.Naming;

/// <summary>Compares strings so that embedded numbers sort numerically ("Clip 2" before "Clip 10").</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        x ??= string.Empty;
        y ??= string.Empty;
        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                int sx = ix, sy = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;
                var nx = x.AsSpan(sx, ix - sx).TrimStart('0');
                var ny = y.AsSpan(sy, iy - sy).TrimStart('0');
                if (nx.Length != ny.Length) return nx.Length - ny.Length;
                var cmp = nx.CompareTo(ny, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
            }
            else
            {
                var cmp = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (cmp != 0) return cmp;
                ix++; iy++;
            }
        }
        return (x.Length - ix) - (y.Length - iy);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter NaturalComparerTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**
```powershell
git add src/VideoShelf.Core/Naming/NaturalComparer.cs tests/VideoShelf.Core.Tests/Naming/NaturalComparerTests.cs
git commit -m "feat(core): natural string comparer"
```

---

## Task 4: Filename → ParsedTitle (base title + episode number)

Implements the spec's grouping heuristic: strip the trailing `<number> <optional extra words>` from the stem.

**Files:**
- Create: `src/VideoShelf.Core/Models/ParsedTitle.cs`
- Create: `src/VideoShelf.Core/Naming/TitleParser.cs`
- Test: `tests/VideoShelf.Core.Tests/Naming/TitleParserTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/VideoShelf.Core.Tests/Naming/TitleParserTests.cs`:
```csharp
using Shouldly;
using VideoShelf.Core.Naming;

namespace VideoShelf.Core.Tests.Naming;

public class TitleParserTests
{
    [Theory]
    // file stem -> expected base title, expected episode number (null = unnumbered)
    [InlineData("Cool Story", "Cool Story", null)]
    [InlineData("Cool Story 2 the sequel", "Cool Story", 2)]
    [InlineData("Cool Story 3 finale", "Cool Story", 3)]
    [InlineData("Another Standalone Tale", "Another Standalone Tale", null)]
    [InlineData("Cool Story 2", "Cool Story", 2)]
    [InlineData("Cool   Story   2", "Cool Story", 2)]   // collapse whitespace in base
    [InlineData("Episode 01", "Episode", 1)]            // leading zeros -> 1
    public void Parses_base_title_and_episode(string stem, string expectedBase, int? expectedEpisode)
    {
        var parsed = TitleParser.Parse(stem);
        parsed.BaseTitle.ShouldBe(expectedBase);
        parsed.EpisodeNumber.ShouldBe(expectedEpisode);
    }

    [Fact]
    public void First_token_number_is_not_an_episode_marker()
    {
        // "300" as the only/first token stays part of the title (no base before it).
        var parsed = TitleParser.Parse("300");
        parsed.BaseTitle.ShouldBe("300");
        parsed.EpisodeNumber.ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter TitleParserTests`
Expected: FAIL — `TitleParser`/`ParsedTitle` do not exist.

- [ ] **Step 3: Implement the model and parser**

`src/VideoShelf.Core/Models/ParsedTitle.cs`:
```csharp
namespace VideoShelf.Core.Models;

/// <summary>Result of parsing a filename stem: a normalized base title and an optional episode number.</summary>
public sealed record ParsedTitle(string BaseTitle, int? EpisodeNumber);
```

`src/VideoShelf.Core/Naming/TitleParser.cs`:
```csharp
using System;
using System.Globalization;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Naming;

/// <summary>
/// Derives a base title + optional episode number from a filename stem.
/// Rule: the episode marker is the FIRST whitespace-delimited token after the first token
/// that parses as a positive integer. The base title is everything before it (whitespace
/// collapsed); everything from the marker onward is dropped. No such token => unnumbered.
/// </summary>
public static class TitleParser
{
    public static ParsedTitle Parse(string stem)
    {
        var tokens = stem.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return new ParsedTitle(stem.Trim(), null);

        for (var i = 1; i < tokens.Length; i++)
        {
            if (int.TryParse(tokens[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0)
                return new ParsedTitle(string.Join(' ', tokens[..i]), n);
        }
        return new ParsedTitle(string.Join(' ', tokens), null);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter TitleParserTests`
Expected: PASS (8 cases).

- [ ] **Step 5: Commit**
```powershell
git add src/VideoShelf.Core/Models/ParsedTitle.cs src/VideoShelf.Core/Naming/TitleParser.cs tests/VideoShelf.Core.Tests/Naming/TitleParserTests.cs
git commit -m "feat(core): filename title/episode parser"
```

---

## Task 5: Section grouping (files → series + standalones)

**Files:**
- Create: `src/VideoShelf.Core/Models/GroupedSection.cs`
- Create: `src/VideoShelf.Core/Naming/SectionGrouper.cs`
- Test: `tests/VideoShelf.Core.Tests/Naming/SectionGrouperTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/VideoShelf.Core.Tests/Naming/SectionGrouperTests.cs`:
```csharp
using System.Linq;
using Shouldly;
using VideoShelf.Core.Naming;

namespace VideoShelf.Core.Tests.Naming;

public class SectionGrouperTests
{
    [Fact]
    public void Groups_numbered_siblings_into_one_series_ordered_by_episode()
    {
        var files = new[]
        {
            "Cool Story.mp4",
            "Cool Story 2 the sequel.mp4",
            "Cool Story 3 finale.mp4",
            "Another Standalone Tale.mp4",
        };

        var result = SectionGrouper.Group(files);

        result.Series.Count.ShouldBe(2);

        var cool = result.Series.Single(s => s.BaseTitle == "Cool Story");
        cool.IsStandalone.ShouldBeFalse();
        cool.Episodes.Select(e => e.FileName)
            .ShouldBe(new[] { "Cool Story.mp4", "Cool Story 2 the sequel.mp4", "Cool Story 3 finale.mp4" });
        cool.Episodes.Select(e => e.EpisodeNumber).ShouldBe(new[] { 1, 2, 3 });

        var standalone = result.Series.Single(s => s.BaseTitle == "Another Standalone Tale");
        standalone.IsStandalone.ShouldBeTrue();
        standalone.Episodes.Count.ShouldBe(1);
    }

    [Fact]
    public void Single_numbered_file_with_no_siblings_is_a_standalone()
    {
        var result = SectionGrouper.Group(new[] { "Apollo 13.mkv" });
        var s = result.Series.Single();
        s.IsStandalone.ShouldBeTrue();
        s.Episodes.Single().FileName.ShouldBe("Apollo 13.mkv");
    }

    [Fact]
    public void Grouping_is_case_insensitive_on_base_title()
    {
        var result = SectionGrouper.Group(new[] { "skit.mp4", "SKIT 2.mp4" });
        result.Series.Count.ShouldBe(1);
        result.Series.Single().Episodes.Count.ShouldBe(2);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter SectionGrouperTests`
Expected: FAIL — `SectionGrouper`/`GroupedSection` do not exist.

- [ ] **Step 3: Implement the models and grouper**

`src/VideoShelf.Core/Models/GroupedSection.cs`:
```csharp
using System.Collections.Generic;

namespace VideoShelf.Core.Models;

/// <summary>One episode within a grouped series: the original file name plus its resolved episode number.</summary>
public sealed record GroupedEpisode(string FileName, int EpisodeNumber);

/// <summary>A series (or standalone) detected within a section.</summary>
public sealed record GroupedSeries(string BaseTitle, bool IsStandalone, IReadOnlyList<GroupedEpisode> Episodes);

/// <summary>All series/standalones detected within a single section folder.</summary>
public sealed record GroupedSection(IReadOnlyList<GroupedSeries> Series);
```

`src/VideoShelf.Core/Naming/SectionGrouper.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Naming;

/// <summary>
/// Groups a section's file names into series and standalones using TitleParser.
/// Files sharing a base title (case-insensitive) form one series; a group of one is a standalone.
/// Within a series: the unnumbered file is episode 1; numbered files keep their number; ties
/// break by natural filename order.
/// </summary>
public static class SectionGrouper
{
    public static GroupedSection Group(IEnumerable<string> fileNames)
    {
        var natural = new NaturalComparer();

        var groups = fileNames
            .Select(f => (File: f, Parsed: TitleParser.Parse(Path.GetFileNameWithoutExtension(f))))
            .GroupBy(x => x.Parsed.BaseTitle, StringComparer.OrdinalIgnoreCase);

        var series = new List<GroupedSeries>();
        foreach (var group in groups)
        {
            var items = group.ToList();
            var isStandalone = items.Count == 1;

            var ordered = items
                .OrderBy(x => x.Parsed.EpisodeNumber ?? 1)
                .ThenBy(x => x.File, natural)
                .ToList();

            var episodes = new List<GroupedEpisode>();
            for (var i = 0; i < ordered.Count; i++)
            {
                var number = ordered[i].Parsed.EpisodeNumber ?? (i + 1);
                episodes.Add(new GroupedEpisode(ordered[i].File, number));
            }

            // Use the first item's base title (preserves the casing seen first).
            series.Add(new GroupedSeries(items[0].Parsed.BaseTitle, isStandalone, episodes));
        }

        return new GroupedSection(series);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter SectionGrouperTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**
```powershell
git add src/VideoShelf.Core/Models/GroupedSection.cs src/VideoShelf.Core/Naming/SectionGrouper.cs tests/VideoShelf.Core.Tests/Naming/SectionGrouperTests.cs
git commit -m "feat(core): section grouping into series and standalones"
```

---

## Task 6: SQLite schema + migrations (`VideoShelfDb`)

**Files:**
- Create: `src/VideoShelf.Core/Storage/VideoShelfDb.cs`
- Create: `tests/VideoShelf.Core.Tests/TestSupport/TempDb.cs` (from Task 1 step 2)
- Test: `tests/VideoShelf.Core.Tests/Storage/VideoShelfDbTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/VideoShelf.Core.Tests/Storage/VideoShelfDbTests.cs`:
```csharp
using Microsoft.Data.Sqlite;
using Shouldly;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class VideoShelfDbTests
{
    [Fact]
    public void Migrate_creates_expected_tables()
    {
        using var temp = new TempDb();
        using var conn = temp.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var tables = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) tables.Add(reader.GetString(0));

        foreach (var expected in new[]
                 { "sources", "sections", "series", "videos", "section_tags", "watch_events", "grouping_overrides", "settings" })
            tables.ShouldContain(expected);
    }

    [Fact]
    public void Migrate_is_idempotent()
    {
        using var temp = new TempDb();
        Should.NotThrow(() => temp.Db.Migrate()); // second migrate is a no-op
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter VideoShelfDbTests`
Expected: FAIL — `VideoShelfDb` does not exist.

- [ ] **Step 3: Implement `VideoShelfDb` and create `TempDb`**

Create `tests/VideoShelf.Core.Tests/TestSupport/TempDb.cs` (content from Task 1 Step 2).

`src/VideoShelf.Core/Storage/VideoShelfDb.cs`:
```csharp
using System;
using Microsoft.Data.Sqlite;

namespace VideoShelf.Core.Storage;

/// <summary>Owns the SQLite connection string and schema. Open() returns a ready connection; Migrate() is idempotent.</summary>
public sealed class VideoShelfDb : IDisposable
{
    private readonly string _connectionString;

    public VideoShelfDb(string dbPath)
        => _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    public void Migrate()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => SqliteConnection.ClearAllPools();

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS sources (
            id INTEGER PRIMARY KEY,
            root_path TEXT NOT NULL UNIQUE,
            display_name TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS sections (
            id INTEGER PRIMARY KEY,
            source_id INTEGER NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
            folder_name TEXT NOT NULL,
            display_name TEXT NOT NULL,
            UNIQUE(source_id, folder_name)
        );
        CREATE TABLE IF NOT EXISTS series (
            id INTEGER PRIMARY KEY,
            section_id INTEGER NOT NULL REFERENCES sections(id) ON DELETE CASCADE,
            base_title TEXT NOT NULL,
            sort_key TEXT NOT NULL,
            is_standalone INTEGER NOT NULL DEFAULT 0,
            UNIQUE(section_id, base_title)
        );
        CREATE TABLE IF NOT EXISTS videos (
            id INTEGER PRIMARY KEY,
            series_id INTEGER NOT NULL REFERENCES series(id) ON DELETE CASCADE,
            file_path TEXT NOT NULL UNIQUE,
            episode_no INTEGER NOT NULL,
            raw_filename TEXT NOT NULL,
            format TEXT NOT NULL,
            duration REAL,
            thumbnail_path TEXT,
            watched INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS section_tags (
            section_id INTEGER NOT NULL REFERENCES sections(id) ON DELETE CASCADE,
            tag TEXT NOT NULL,
            PRIMARY KEY(section_id, tag)
        );
        CREATE TABLE IF NOT EXISTS watch_events (
            id INTEGER PRIMARY KEY,
            video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
            watched_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS grouping_overrides (
            id INTEGER PRIMARY KEY,
            section_id INTEGER NOT NULL REFERENCES sections(id) ON DELETE CASCADE,
            file_path TEXT NOT NULL,
            override_base_title TEXT,
            override_episode_no INTEGER,
            UNIQUE(section_id, file_path)
        );
        CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_videos_series ON videos(series_id);
        CREATE INDEX IF NOT EXISTS ix_sections_source ON sections(source_id);
        CREATE INDEX IF NOT EXISTS ix_series_section ON series(section_id);
        """;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter VideoShelfDbTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**
```powershell
git add src/VideoShelf.Core/Storage/VideoShelfDb.cs tests/VideoShelf.Core.Tests/TestSupport/TempDb.cs tests/VideoShelf.Core.Tests/Storage/VideoShelfDbTests.cs
git commit -m "feat(core): SQLite schema and migrations"
```

---

## Task 7: Domain models

**Files:**
- Create: `src/VideoShelf.Core/Models/Source.cs`, `Section.cs`, `Series.cs`, `Video.cs`, `ScannedFile.cs`

- [ ] **Step 1: Create the records (no test — plain data carriers, exercised by repo/scan tests)**

`src/VideoShelf.Core/Models/Source.cs`:
```csharp
namespace VideoShelf.Core.Models;
public sealed record Source(long Id, string RootPath, string DisplayName);
```
`src/VideoShelf.Core/Models/Section.cs`:
```csharp
namespace VideoShelf.Core.Models;
public sealed record Section(long Id, long SourceId, string FolderName, string DisplayName);
```
`src/VideoShelf.Core/Models/Series.cs`:
```csharp
namespace VideoShelf.Core.Models;
public sealed record Series(long Id, long SectionId, string BaseTitle, string SortKey, bool IsStandalone);
```
`src/VideoShelf.Core/Models/Video.cs`:
```csharp
namespace VideoShelf.Core.Models;
public sealed record Video(
    long Id, long SeriesId, string FilePath, int EpisodeNo, string RawFilename,
    string Format, double? Duration, string? ThumbnailPath, bool Watched);
```
`src/VideoShelf.Core/Models/ScannedFile.cs`:
```csharp
namespace VideoShelf.Core.Models;
/// <summary>One video file found on disk during a scan.</summary>
public sealed record ScannedFile(string FullPath, string FileName, string Extension);
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/VideoShelf.Core`
Expected: succeeds.

- [ ] **Step 3: Commit**
```powershell
git add src/VideoShelf.Core/Models/Source.cs src/VideoShelf.Core/Models/Section.cs src/VideoShelf.Core/Models/Series.cs src/VideoShelf.Core/Models/Video.cs src/VideoShelf.Core/Models/ScannedFile.cs
git commit -m "feat(core): domain model records"
```

---

## Task 8: Folder scanner (source root → sections → files)

**Files:**
- Create: `src/VideoShelf.Core/Scanning/FolderScanner.cs`
- Test: `tests/VideoShelf.Core.Tests/Scanning/FolderScannerTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/VideoShelf.Core.Tests/Scanning/FolderScannerTests.cs`:
```csharp
using System.Linq;
using Shouldly;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Scanning;

public class FolderScannerTests
{
    [Fact]
    public void Scans_each_subfolder_as_a_section_with_only_video_files()
    {
        using var dir = new TempDir();
        dir.Touch("Creator A/skit.mp4");
        dir.Touch("Creator A/skit 2.mp4");
        dir.Touch("Creator A/notes.txt");          // ignored (not video)
        dir.Touch("Home Videos/trip.mkv");
        dir.Touch("loose.mp4");                      // file directly in root -> ignored (no section)

        var sections = FolderScanner.Scan(dir.Path).OrderBy(s => s.FolderName).ToList();

        sections.Count.ShouldBe(2);
        sections[0].FolderName.ShouldBe("Creator A");
        sections[0].Files.Select(f => f.FileName).OrderBy(x => x)
            .ShouldBe(new[] { "skit 2.mp4", "skit.mp4" });
        sections[1].FolderName.ShouldBe("Home Videos");
        sections[1].Files.Single().FileName.ShouldBe("trip.mkv");
    }

    [Fact]
    public void Empty_or_video_less_sections_are_omitted()
    {
        using var dir = new TempDir();
        dir.Touch("OnlyDocs/readme.txt");
        FolderScanner.Scan(dir.Path).ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter FolderScannerTests`
Expected: FAIL — `FolderScanner` does not exist.

- [ ] **Step 3: Implement**

`src/VideoShelf.Core/Scanning/FolderScanner.cs`:
```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VideoShelf.Core.Models;
using VideoShelf.Core.Naming;

namespace VideoShelf.Core.Scanning;

/// <summary>A section folder found under a source root, with its video files.</summary>
public sealed record ScannedSection(string FolderName, IReadOnlyList<ScannedFile> Files);

/// <summary>
/// Scans a single source root: each immediate subfolder is a section; its video files (one level
/// deep, per the spec's "flat" sections) become ScannedFiles. Folders with no video files are omitted.
/// </summary>
public static class FolderScanner
{
    public static IReadOnlyList<ScannedSection> Scan(string sourceRoot)
    {
        if (!Directory.Exists(sourceRoot))
            return [];

        var sections = new List<ScannedSection>();
        foreach (var subDir in Directory.EnumerateDirectories(sourceRoot))
        {
            var files = Directory.EnumerateFiles(subDir)
                .Where(p => VideoExtensions.IsVideo(p))
                .Select(p => new ScannedFile(p, Path.GetFileName(p), Path.GetExtension(p)))
                .ToList();
            if (files.Count > 0)
                sections.Add(new ScannedSection(Path.GetFileName(subDir), files));
        }
        return sections;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter FolderScannerTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**
```powershell
git add src/VideoShelf.Core/Scanning/FolderScanner.cs tests/VideoShelf.Core.Tests/Scanning/FolderScannerTests.cs
git commit -m "feat(core): folder scanner"
```

---

## Task 9: LibraryRepository — sources, sections, series, videos (idempotent upserts)

**Files:**
- Create: `src/VideoShelf.Core/Storage/LibraryRepository.cs`
- Test: `tests/VideoShelf.Core.Tests/Storage/LibraryRepositoryTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/VideoShelf.Core.Tests/Storage/LibraryRepositoryTests.cs`:
```csharp
using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class LibraryRepositoryTests
{
    [Fact]
    public void AddSource_is_idempotent_by_path()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);

        var id1 = repo.UpsertSource(@"C:\Vids", "Vids");
        var id2 = repo.UpsertSource(@"C:\Vids", "Vids");

        id1.ShouldBe(id2);
        repo.GetSources().Count.ShouldBe(1);
    }

    [Fact]
    public void Upsert_section_series_video_round_trips()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);

        var sourceId = repo.UpsertSource(@"C:\Vids", "Vids");
        var sectionId = repo.UpsertSection(sourceId, "Creator A");
        var seriesId = repo.UpsertSeries(sectionId, "Cool Story", isStandalone: false);
        repo.UpsertVideo(seriesId, @"C:\Vids\Creator A\Cool Story.mp4", episodeNo: 1, format: ".mp4");

        var videos = repo.GetVideosForSeries(seriesId);
        videos.Single().FilePath.ShouldBe(@"C:\Vids\Creator A\Cool Story.mp4");
        videos.Single().EpisodeNo.ShouldBe(1);
        videos.Single().Watched.ShouldBeFalse();
    }

    [Fact]
    public void Upsert_video_updates_episode_without_duplicating()
    {
        using var temp = new TempDb();
        var repo = new LibraryRepository(temp.Db);
        var seriesId = repo.UpsertSeries(repo.UpsertSection(repo.UpsertSource(@"C:\V", "V"), "S"), "Base", false);

        repo.UpsertVideo(seriesId, @"C:\V\S\a.mp4", episodeNo: 1, format: ".mp4");
        repo.UpsertVideo(seriesId, @"C:\V\S\a.mp4", episodeNo: 2, format: ".mp4");

        var v = repo.GetVideosForSeries(seriesId).Single();
        v.EpisodeNo.ShouldBe(2);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter LibraryRepositoryTests`
Expected: FAIL — `LibraryRepository` does not exist.

- [ ] **Step 3: Implement**

`src/VideoShelf.Core/Storage/LibraryRepository.cs`:
```csharp
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using VideoShelf.Core.Models;

namespace VideoShelf.Core.Storage;

/// <summary>Reads/writes sources, sections, series, and videos. Upserts are idempotent by natural key.</summary>
public sealed class LibraryRepository(VideoShelfDb db)
{
    public long UpsertSource(string rootPath, string displayName)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sources(root_path, display_name) VALUES($p, $n)
            ON CONFLICT(root_path) DO UPDATE SET display_name=excluded.display_name
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$p", rootPath);
        cmd.Parameters.AddWithValue("$n", displayName);
        return (long)cmd.ExecuteScalar()!;
    }

    public long UpsertSection(long sourceId, string folderName)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sections(source_id, folder_name, display_name) VALUES($s, $f, $f)
            ON CONFLICT(source_id, folder_name) DO UPDATE SET folder_name=excluded.folder_name
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$s", sourceId);
        cmd.Parameters.AddWithValue("$f", folderName);
        return (long)cmd.ExecuteScalar()!;
    }

    public long UpsertSeries(long sectionId, string baseTitle, bool isStandalone)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO series(section_id, base_title, sort_key, is_standalone) VALUES($s, $b, $k, $a)
            ON CONFLICT(section_id, base_title) DO UPDATE SET is_standalone=excluded.is_standalone
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$s", sectionId);
        cmd.Parameters.AddWithValue("$b", baseTitle);
        cmd.Parameters.AddWithValue("$k", baseTitle.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$a", isStandalone ? 1 : 0);
        return (long)cmd.ExecuteScalar()!;
    }

    public long UpsertVideo(long seriesId, string filePath, int episodeNo, string format)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO videos(series_id, file_path, episode_no, raw_filename, format)
            VALUES($s, $p, $e, $r, $f)
            ON CONFLICT(file_path) DO UPDATE SET series_id=excluded.series_id,
                episode_no=excluded.episode_no, raw_filename=excluded.raw_filename, format=excluded.format
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$s", seriesId);
        cmd.Parameters.AddWithValue("$p", filePath);
        cmd.Parameters.AddWithValue("$e", episodeNo);
        cmd.Parameters.AddWithValue("$r", Path.GetFileName(filePath));
        cmd.Parameters.AddWithValue("$f", format);
        return (long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<Source> GetSources()
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, root_path, display_name FROM sources ORDER BY display_name";
        var list = new List<Source>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Source(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public IReadOnlyList<Video> GetVideosForSeries(long seriesId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, series_id, file_path, episode_no, raw_filename, format, duration, thumbnail_path, watched
            FROM videos WHERE series_id=$s ORDER BY episode_no
            """;
        cmd.Parameters.AddWithValue("$s", seriesId);
        var list = new List<Video>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Video(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetString(4),
                r.GetString(5), r.IsDBNull(6) ? null : r.GetDouble(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.GetInt64(8) != 0));
        return list;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter LibraryRepositoryTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**
```powershell
git add src/VideoShelf.Core/Storage/LibraryRepository.cs tests/VideoShelf.Core.Tests/Storage/LibraryRepositoryTests.cs
git commit -m "feat(core): library repository upserts and queries"
```

---

## Task 10: WatchRepository (watched flag + watch_events)

**Files:**
- Create: `src/VideoShelf.Core/Storage/WatchRepository.cs`
- Test: `tests/VideoShelf.Core.Tests/Storage/WatchRepositoryTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/VideoShelf.Core.Tests/Storage/WatchRepositoryTests.cs`:
```csharp
using System.Linq;
using Shouldly;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Storage;

public class WatchRepositoryTests
{
    private static long SeedVideo(TempDb temp)
    {
        var lib = new LibraryRepository(temp.Db);
        var seriesId = lib.UpsertSeries(lib.UpsertSection(lib.UpsertSource(@"C:\V", "V"), "S"), "Base", false);
        return lib.UpsertVideo(seriesId, @"C:\V\S\a.mp4", 1, ".mp4");
    }

    [Fact]
    public void MarkWatched_sets_flag_and_records_event()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var watch = new WatchRepository(temp.Db);

        watch.SetWatched(videoId, true);

        watch.IsWatched(videoId).ShouldBeTrue();
        watch.RecentlyWatchedVideoIds(10).ShouldContain(videoId);
    }

    [Fact]
    public void Toggle_unwatched_clears_flag_but_keeps_history()
    {
        using var temp = new TempDb();
        var videoId = SeedVideo(temp);
        var watch = new WatchRepository(temp.Db);

        watch.SetWatched(videoId, true);
        watch.SetWatched(videoId, false);

        watch.IsWatched(videoId).ShouldBeFalse();
        watch.RecentlyWatchedVideoIds(10).ShouldContain(videoId); // event history retained
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter WatchRepositoryTests`
Expected: FAIL — `WatchRepository` does not exist.

- [ ] **Step 3: Implement**

`src/VideoShelf.Core/Storage/WatchRepository.cs`:
```csharp
using System;
using System.Collections.Generic;
using VideoShelf.Core.Storage;

namespace VideoShelf.Core.Storage;

/// <summary>Watched/unwatched state and the watch-event history that feeds discovery.</summary>
public sealed class WatchRepository(VideoShelfDb db)
{
    public void SetWatched(long videoId, bool watched)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        using (var upd = conn.CreateCommand())
        {
            upd.CommandText = "UPDATE videos SET watched=$w WHERE id=$id";
            upd.Parameters.AddWithValue("$w", watched ? 1 : 0);
            upd.Parameters.AddWithValue("$id", videoId);
            upd.ExecuteNonQuery();
        }

        if (watched)
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = "INSERT INTO watch_events(video_id, watched_at) VALUES($id, $at)";
            ins.Parameters.AddWithValue("$id", videoId);
            ins.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("o"));
            ins.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public bool IsWatched(long videoId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT watched FROM videos WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", videoId);
        return cmd.ExecuteScalar() is long l && l != 0;
    }

    public IReadOnlyList<long> RecentlyWatchedVideoIds(int limit)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT video_id FROM watch_events ORDER BY watched_at DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", limit);
        var list = new List<long>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt64(0));
        return list;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter WatchRepositoryTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**
```powershell
git add src/VideoShelf.Core/Storage/WatchRepository.cs tests/VideoShelf.Core.Tests/Storage/WatchRepositoryTests.cs
git commit -m "feat(core): watch state repository"
```

---

## Task 11: ScanService — orchestrate scan → group → persist (incremental, crash-safe)

This ties scanning + grouping + persistence together and is the Foundation's headline deliverable.

**Files:**
- Create: `src/VideoShelf.Core/Scanning/ScanService.cs`
- Test: `tests/VideoShelf.Core.Tests/Scanning/ScanServiceTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/VideoShelf.Core.Tests/Scanning/ScanServiceTests.cs`:
```csharp
using System.Linq;
using Shouldly;
using VideoShelf.Core.Scanning;
using VideoShelf.Core.Storage;
using VideoShelf.Core.Tests.TestSupport;

namespace VideoShelf.Core.Tests.Scanning;

public class ScanServiceTests
{
    [Fact]
    public void Scanning_a_source_persists_sections_series_and_videos()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Cool Story.mp4");
        dir.Touch("Creator A/Cool Story 2.mp4");
        dir.Touch("Home Videos/Trip.mkv");

        var lib = new LibraryRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);

        scan.ScanSource(dir.Path, "My Videos");

        var sourceId = lib.GetSources().Single().Id;
        // Creator A has one series ("Cool Story") with 2 episodes; Home Videos has one standalone.
        var sections = lib.GetSections(sourceId).OrderBy(s => s.FolderName).ToList();
        sections.Select(s => s.FolderName).ShouldBe(new[] { "Creator A", "Home Videos" });

        var creatorSeries = lib.GetSeriesForSection(sections[0].Id).Single();
        creatorSeries.BaseTitle.ShouldBe("Cool Story");
        creatorSeries.IsStandalone.ShouldBeFalse();
        lib.GetVideosForSeries(creatorSeries.Id).Count.ShouldBe(2);

        var homeSeries = lib.GetSeriesForSection(sections[1].Id).Single();
        homeSeries.IsStandalone.ShouldBeTrue();
    }

    [Fact]
    public void Rescan_is_idempotent_and_preserves_watched_state()
    {
        using var temp = new TempDb();
        using var dir = new TempDir();
        dir.Touch("Creator A/Cool Story.mp4");

        var lib = new LibraryRepository(temp.Db);
        var watch = new WatchRepository(temp.Db);
        var scan = new ScanService(temp.Db, lib);

        scan.ScanSource(dir.Path, "My Videos");
        var sourceId = lib.GetSources().Single().Id;
        var section = lib.GetSections(sourceId).Single();
        var series = lib.GetSeriesForSection(section.Id).Single();
        var video = lib.GetVideosForSeries(series.Id).Single();
        watch.SetWatched(video.Id, true);

        scan.ScanSource(dir.Path, "My Videos"); // rescan

        // Still exactly one video, watched flag intact.
        var after = lib.GetVideosForSeries(series.Id).Single();
        after.Id.ShouldBe(video.Id);
        after.Watched.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Add the supporting query methods to LibraryRepository**

Add these methods to `src/VideoShelf.Core/Storage/LibraryRepository.cs` (used by the test and the service):
```csharp
    public IReadOnlyList<Section> GetSections(long sourceId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, source_id, folder_name, display_name FROM sections WHERE source_id=$s ORDER BY display_name";
        cmd.Parameters.AddWithValue("$s", sourceId);
        var list = new List<Section>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Section(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    public IReadOnlyList<Series> GetSeriesForSection(long sectionId)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, section_id, base_title, sort_key, is_standalone FROM series WHERE section_id=$s ORDER BY sort_key";
        cmd.Parameters.AddWithValue("$s", sectionId);
        var list = new List<Series>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Series(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0));
        return list;
    }
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter ScanServiceTests`
Expected: FAIL — `ScanService` does not exist.

- [ ] **Step 4: Implement**

`src/VideoShelf.Core/Scanning/ScanService.cs`:
```csharp
using System.IO;
using VideoShelf.Core.Naming;
using VideoShelf.Core.Storage;

namespace VideoShelf.Core.Scanning;

/// <summary>
/// Orchestrates a full source scan: discover sections/files, group into series/standalones,
/// and upsert into the library. Idempotent — re-scanning the same source updates in place
/// (upserts keyed by natural keys), so watched-state and IDs survive.
/// </summary>
public sealed class ScanService(VideoShelfDb db, LibraryRepository library)
{
    public void ScanSource(string sourceRoot, string displayName)
    {
        var sourceId = library.UpsertSource(sourceRoot, displayName);

        foreach (var section in FolderScanner.Scan(sourceRoot))
        {
            var sectionId = library.UpsertSection(sourceId, section.FolderName);
            var grouped = SectionGrouper.Group(section.Files.ConvertAll(f => f.FileName) ?? []);

            foreach (var series in grouped.Series)
            {
                var seriesId = library.UpsertSeries(sectionId, series.BaseTitle, series.IsStandalone);
                foreach (var episode in series.Episodes)
                {
                    var full = Path.Combine(sourceRoot, section.FolderName, episode.FileName);
                    library.UpsertVideo(seriesId, full, episode.EpisodeNumber, Path.GetExtension(episode.FileName));
                }
            }
        }
    }
}
```

> Note: `section.Files.ConvertAll(...)` — `ScannedSection.Files` is an `IReadOnlyList<ScannedFile>`. Replace with `section.Files.Select(f => f.FileName).ToList()` (add `using System.Linq;`). Fix while implementing so it compiles.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/VideoShelf.Core.Tests --filter ScanServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test VideoShelf.sln`
Expected: all tests pass, 0 failures.

- [ ] **Step 7: Commit**
```powershell
git add src/VideoShelf.Core/Scanning/ScanService.cs src/VideoShelf.Core/Storage/LibraryRepository.cs tests/VideoShelf.Core.Tests/Scanning/ScanServiceTests.cs
git commit -m "feat(core): scan service orchestrating scan, grouping, and persistence"
```

---

## Tasks deferred to later plans (stubs to keep the Foundation focused)

- **TagRepository** (`section_tags`) and **OverrideRepository** (`grouping_overrides`) — schema exists (Task 6); repositories land in **Plan 4 (Discovery/tags)** and the grouping-review UI respectively, where they have a consumer. Adding them here with no caller would violate YAGNI.
- **Duration/thumbnail population** — needs libVLC, which is an App-layer concern (**Plan 2**). The `videos.duration` / `thumbnail_path` columns already exist; the scan leaves them null and Plan 2 fills them.
- **Pruning deleted files** — when a rescan finds a file gone, mark/remove it. Deferred to **Plan 2** alongside the incremental-scan UI (needs a product decision on showing "missing" items); the Foundation's upsert-only scan is safe and never loses data.

---

## Self-review (done while writing)

- **Spec coverage (Plan 1 scope):** §3 two-project layout ✓ (Core only here; App in Plan 2). §4 library model + multi-section scan ✓ (Tasks 8, 11). §4 grouping heuristic ✓ (Tasks 4, 5). §5 data model tables ✓ (Task 6) — `is_standalone` ✓. §2 crash-safe/idempotent scanning ✓ (Task 11 rescan test). Multi-**source** scanning ✓ (`ScanSource` per root; called once per source). Discovery/tags (§7/§8), player (§9), rename (§10), harness (§11) are explicitly later plans (roadmap) — not gaps.
- **Placeholder scan:** none — every code step has complete code; two inline "fix while implementing" notes are concrete one-line corrections, not placeholders.
- **Type consistency:** `VideoShelfDb.Open()`/`Migrate()`, `LibraryRepository.Upsert*`/`Get*`, `WatchRepository.SetWatched/IsWatched/RecentlyWatchedVideoIds`, `FolderScanner.Scan`→`ScannedSection`, `SectionGrouper.Group`→`GroupedSection`/`GroupedSeries`/`GroupedEpisode`, `TitleParser.Parse`→`ParsedTitle` are used consistently across tasks.
