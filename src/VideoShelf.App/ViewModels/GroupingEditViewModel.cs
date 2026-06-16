using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoShelf.Core.Storage;

namespace VideoShelf.App.ViewModels;

/// <summary>
/// M18-H: Grouping override operations exposed on the creator-page Edit mode.
/// Wraps <see cref="LibraryRepository.SetGroupingOverride"/>,
/// <see cref="LibraryRepository.ClearGroupingOverride"/>, and
/// <see cref="LibraryRepository.RegroupSection"/> so the VM stays pure and
/// unit-testable without a full scan.
/// <para>
/// Supported operations:
/// <list type="bullet">
///   <item><b>Move episode to series</b> — sets <c>override_base_title</c> for one file
///         to the given target series title, then calls RegroupSection.</item>
///   <item><b>Reorder episode</b> — sets <c>override_episode_no</c> for one file to a
///         new number (base title preserved via null), then calls RegroupSection.</item>
///   <item><b>Reset grouping</b> — clears the override for one file (or all files in a
///         series), then calls RegroupSection.</item>
/// </list>
/// </para>
/// After any mutation <see cref="RegroupRequested"/> is raised so the owning VM can
/// reload the section page.
/// </summary>
public sealed partial class GroupingEditViewModel : ObservableObject
{
    private readonly LibraryRepository _library;
    private long _sectionId;

    public GroupingEditViewModel(LibraryRepository library)
    {
        _library = library;
    }

    /// <summary>Must be called (by the host VM) before any commands are used.</summary>
    public void Attach(long sectionId) => _sectionId = sectionId;

    /// <summary>Raised after any override write + RegroupSection so the host VM can reload.</summary>
    public event EventHandler? RegroupRequested;

    // ── Move episode to a different series ───────────────────────────────────

    /// <summary>
    /// Moves a single episode (by full file path) to <paramref name="targetSeriesTitle"/>.
    /// Sets <c>override_base_title</c>, then regroups the section.
    /// </summary>
    [RelayCommand]
    public void MoveEpisodeToSeries(MoveEpisodeArgs args)
    {
        if (_sectionId <= 0) return;
        _library.SetGroupingOverride(_sectionId, args.FilePath, args.TargetSeriesTitle, null);
        _library.RegroupSection(_sectionId);
        RegroupRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Reorder an episode ───────────────────────────────────────────────────

    /// <summary>
    /// Sets a manual episode number for the given file path (base title is preserved as
    /// null so only the ordering is overridden), then regroups.
    /// </summary>
    [RelayCommand]
    public void SetEpisodeOrder(SetEpisodeOrderArgs args)
    {
        if (_sectionId <= 0) return;
        _library.SetGroupingOverride(_sectionId, args.FilePath, null, args.NewEpisodeNo);
        _library.RegroupSection(_sectionId);
        RegroupRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Reset grouping ───────────────────────────────────────────────────────

    /// <summary>Clears the grouping override for a single file path, then regroups.</summary>
    [RelayCommand]
    public void ResetEpisodeGrouping(string filePath)
    {
        if (_sectionId <= 0) return;
        _library.ClearGroupingOverride(_sectionId, filePath);
        _library.RegroupSection(_sectionId);
        RegroupRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears overrides for all files in a series (by supplying their paths), then regroups.</summary>
    [RelayCommand]
    public void ResetSeriesGrouping(IEnumerable<string> filePaths)
    {
        if (_sectionId <= 0) return;
        foreach (var fp in filePaths)
            _library.ClearGroupingOverride(_sectionId, fp);
        _library.RegroupSection(_sectionId);
        RegroupRequested?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Args for <see cref="GroupingEditViewModel.MoveEpisodeToSeriesCommand"/>.</summary>
public sealed record MoveEpisodeArgs(string FilePath, string TargetSeriesTitle);

/// <summary>Args for <see cref="GroupingEditViewModel.SetEpisodeOrderCommand"/>.</summary>
public sealed record SetEpisodeOrderArgs(string FilePath, int NewEpisodeNo);
