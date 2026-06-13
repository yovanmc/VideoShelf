using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoShelf.App.ViewModels;

public enum PaletteItemKind { Action, Creator, Video }

/// <summary>A single result row in the command palette list.</summary>
public sealed partial class PaletteItemViewModel : ObservableObject
{
    /// <summary>Display label shown in the palette.</summary>
    public string Label { get; }

    /// <summary>Secondary sub-label (e.g., series title for a video item). Null for actions.</summary>
    public string? SubLabel { get; }

    /// <summary>WPF-UI SymbolRegular member name for the row icon (e.g., "Home24").</summary>
    public string IconSymbol { get; }

    /// <summary>What kind of result this is.</summary>
    public PaletteItemKind Kind { get; }

    /// <summary>The ranked score (used to sort the list). Not bound in the view.</summary>
    public double Score { get; }

    /// <summary>Action to execute when the user selects this item.</summary>
    public Action Execute { get; }

    public PaletteItemViewModel(
        string label,
        string iconSymbol,
        PaletteItemKind kind,
        Action execute,
        double score = 1.0,
        string? subLabel = null)
    {
        Label = label;
        SubLabel = subLabel;
        IconSymbol = iconSymbol;
        Kind = kind;
        Execute = execute;
        Score = score;
    }
}
