using System;

namespace VideoShelf.App.ViewModels;

/// <summary>Event args carrying the letter the user clicked in the A–Z jump strip.</summary>
public sealed class JumpLetterArgs : EventArgs
{
    public char Letter { get; }
    public JumpLetterArgs(char letter) => Letter = letter;
}
