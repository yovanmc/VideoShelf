// src/VideoShelf.App/ViewModels/RenameRowViewModel.cs
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoShelf.Core.Renaming;

namespace VideoShelf.App.ViewModels;

/// <summary>One editable row in the rename preview: current name, proposed name, resolved status.</summary>
public sealed partial class RenameRowViewModel : ObservableObject
{
    public long VideoId { get; }
    public int EpisodeNo { get; }
    public string OldName { get; }

    [ObservableProperty] private string _newName;
    [ObservableProperty] private RenameItemStatus _status;

    public event EventHandler? NewNameEdited;

    public RenameRowViewModel(long videoId, int episodeNo, string oldName, string proposedName, RenameItemStatus status)
    {
        VideoId = videoId;
        EpisodeNo = episodeNo;
        OldName = oldName;
        _newName = proposedName;
        _status = status;
    }

    public bool WillRename => Status == RenameItemStatus.Ready;

    public string StatusText => Status switch
    {
        RenameItemStatus.Ready => "Will rename",
        RenameItemStatus.Unchanged => "Unchanged",
        RenameItemStatus.SourceMissing => "Source missing",
        RenameItemStatus.TargetExists => "Target exists",
        RenameItemStatus.DuplicateTarget => "Duplicate target",
        RenameItemStatus.InvalidName => "Invalid name",
        _ => "",
    };

    partial void OnStatusChanged(RenameItemStatus value)
    {
        OnPropertyChanged(nameof(WillRename));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnNewNameChanged(string value) => NewNameEdited?.Invoke(this, EventArgs.Empty);
}
