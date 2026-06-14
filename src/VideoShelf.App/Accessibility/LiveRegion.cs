using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace VideoShelf.App.Accessibility;

/// <summary>
/// Attached behavior that turns any <see cref="TextBlock"/> into a WAI-ARIA-equivalent live
/// region: when the bound text changes to a non-empty value that differs from the previous
/// value, it raises <see cref="AutomationEvents.LiveRegionChanged"/> so screen-readers
/// (Narrator, JAWS, NVDA) announce the new text without the user having to navigate to it.
///
/// Usage in XAML:
/// <code>
///   &lt;TextBlock acc:LiveRegion.Text="{Binding ScanStatus}"
///              acc:LiveRegion.Politeness="Polite" /&gt;
/// </code>
///
/// <b>Important:</b> <c>LiveRegion.Text</c> sets the TextBlock's <c>.Text</c> property
/// programmatically (the attached handler owns the text surface).  Do NOT attach
/// <c>LiveRegion.Text</c> to the same element whose <c>Text</c> you also bind to a visible
/// label you want to preserve — use a dedicated, visually-minimal TextBlock instead.
/// </summary>
public static class LiveRegion
{
    // ── Text property ────────────────────────────────────────────────────────
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(LiveRegion),
            new PropertyMetadata(null, OnTextChanged));

    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);
    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);

    // ── Politeness property ──────────────────────────────────────────────────
    public static readonly DependencyProperty PolitenessProperty =
        DependencyProperty.RegisterAttached(
            "Politeness", typeof(AutomationLiveSetting), typeof(LiveRegion),
            new PropertyMetadata(AutomationLiveSetting.Polite));

    public static void SetPoliteness(DependencyObject d, AutomationLiveSetting v) => d.SetValue(PolitenessProperty, v);
    public static AutomationLiveSetting GetPoliteness(DependencyObject d) => (AutomationLiveSetting)d.GetValue(PolitenessProperty);

    // ── Decision helper (pure, testable without WPF) ────────────────────────

    /// <summary>
    /// Returns true when a live-region announcement should be raised:
    /// the new text is non-empty AND it differs from the old text.
    /// Empty/null transitions are intentionally silent (e.g. clearing a status bar
    /// should not announce an empty string to the user).
    /// </summary>
    public static bool ShouldAnnounce(string? oldText, string? newText)
        => !string.IsNullOrEmpty(newText) &&
           !string.Equals(oldText, newText, System.StringComparison.Ordinal);

    // ── Change handler ───────────────────────────────────────────────────────

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;

        // Write the new text to the element (the attached behavior owns this surface).
        tb.Text = (string?)e.NewValue ?? "";

        // Apply the live-setting so AT knows how urgently to announce this region.
        AutomationProperties.SetLiveSetting(tb, GetPoliteness(d));

        // Raise the UIA LiveRegionChanged event only when warranted.
        if (!ShouldAnnounce(e.OldValue as string, e.NewValue as string)) return;

        var peer = UIElementAutomationPeer.FromElement(tb)
                   ?? UIElementAutomationPeer.CreatePeerForElement(tb);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
