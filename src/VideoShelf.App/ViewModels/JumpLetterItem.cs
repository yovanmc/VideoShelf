namespace VideoShelf.App.ViewModels;

/// <summary>
/// A single entry in the A–Z jump strip.
/// Exposes whether a creator exists with a name starting with <see cref="Letter"/>
/// so the strip can visually grey out unavailable letters.
/// </summary>
public sealed class JumpLetterItem
{
    public char Letter { get; }
    public bool IsAvailable { get; }

    public JumpLetterItem(char letter, bool isAvailable)
    {
        Letter = letter;
        IsAvailable = isAvailable;
    }
}
