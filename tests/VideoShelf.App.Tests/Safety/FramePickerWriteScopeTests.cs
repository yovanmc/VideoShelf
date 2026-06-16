using System;
using System.IO;
using Shouldly;
using VideoShelf.App.Services;
using VideoShelf.App.ViewModels;
using Xunit;

namespace VideoShelf.App.Tests.Safety;

/// <summary>
/// B4 — pinning tests for the creator frame-picker write-scope invariant.
///
/// "The library is never written." A captured creator portrait frame is composed via two
/// production helpers and must ALWAYS land under the app-data covers directory, never under
/// any source/library root:
///   * <see cref="AppPaths.CoversDirectory"/> = <c>&lt;dataDir&gt;/covers</c>
///   * <see cref="CreatorFramePickerViewModel.BuildCandidateFramePath"/> appends
///     <c>creator_&lt;sectionId&gt;_&lt;guid:N&gt;.png</c> under that covers dir.
///
/// (The source video itself is opened read-only by the IThumbnailSnapshotter; this test
/// pins the OUTPUT path scope, which is the only place the picker writes bytes.)
/// </summary>
public sealed class FramePickerWriteScopeTests
{
    /// <summary>Mirrors how DI composes the cover output path (AppPaths + BuildCandidateFramePath).</summary>
    private static string ComposeCoverPath(string dataDir, long sectionId)
    {
        var coversDir = new AppPaths(dataDir).CoversDirectory;
        return CreatorFramePickerViewModel.BuildCandidateFramePath(sectionId, coversDir);
    }

    [Fact]
    public void CoverPath_IsAlwaysUnderDataDirCovers()
    {
        var dataDir = @"C:\Users\user\AppData\Local\VideoShelf";
        var expectedCovers = Path.Combine(dataDir, "covers");

        var path = ComposeCoverPath(dataDir, sectionId: 7);

        path.ShouldStartWith(expectedCovers + Path.DirectorySeparatorChar);
        path.ShouldEndWith(".png");
    }

    [Fact]
    public void CoverFileName_FollowsCreatorSectionGuidConvention()
    {
        var path = ComposeCoverPath(@"D:\app-data", sectionId: 42);
        var name = Path.GetFileName(path);

        name.ShouldStartWith("creator_42_");
        name.ShouldEndWith(".png");
        // creator_<sectionId>_<guid:N>.png  →  guid:N is 32 hex chars, no dashes.
        var guidPart = name["creator_42_".Length..^".png".Length];
        guidPart.Length.ShouldBe(32);
        guidPart.ShouldNotContain("-");
        Guid.TryParseExact(guidPart, "N", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(@"D:\MyVideos")]
    [InlineData(@"D:\MyVideos\Creator\Show")]
    [InlineData(@"E:\Library")]
    [InlineData(@"\\NAS\media")]
    public void CoverPath_IsNeverUnderAnyLibraryRoot(string libraryRoot)
    {
        // App data lives on a different tree than the library.
        var dataDir = @"C:\Users\user\AppData\Local\VideoShelf";

        var path = ComposeCoverPath(dataDir, sectionId: 3);

        path.ShouldNotStartWith(libraryRoot, Case.Insensitive,
            "a creator cover must never be written inside a library/source folder");
    }

    [Fact]
    public void CoverPath_IsUnderCovers_EvenWhenDataDirSitsBesideLibrary()
    {
        // Pathological: data dir and library share a parent. The covers subfolder still scopes it.
        var parent     = @"C:\shared";
        var dataDir    = Path.Combine(parent, "VideoShelf");
        var libraryRoot = Path.Combine(parent, "MyVideos");

        var path = ComposeCoverPath(dataDir, sectionId: 1);

        path.ShouldStartWith(Path.Combine(dataDir, "covers") + Path.DirectorySeparatorChar);
        path.ShouldNotStartWith(libraryRoot, Case.Insensitive);
    }

    [Fact]
    public void BuildCandidateFramePath_IsUniqueAcrossCalls()
    {
        var a = CreatorFramePickerViewModel.BuildCandidateFramePath(1, @"C:\app\covers");
        var b = CreatorFramePickerViewModel.BuildCandidateFramePath(1, @"C:\app\covers");
        a.ShouldNotBe(b, "the guid suffix prevents concurrent captures from colliding");
    }
}
