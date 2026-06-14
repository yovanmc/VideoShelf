using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VideoShelf.App.Harness;

public static class A11yTreeDumper
{
    public static void Dump(Window window, string path)
    {
        var sb = new StringBuilder();
        var peer = UIElementAutomationPeer.CreatePeerForElement(window)
                   ?? new WindowAutomationPeer(window);
        Walk(peer, 0, sb);

        // ── Container keyboard-nav section ────────────────────────────────────
        // Walk the visual tree and print KeyboardNavigation + TextSearch attached
        // values for all ItemsControl and ListBox containers.
        sb.AppendLine();
        sb.AppendLine("=== Container keyboard-nav attrs (visual tree walk) ===");
        AppendContainerNavAttrs(window, sb);

        // ── Live-region section (D2) ──────────────────────────────────────────
        // Walk the visual tree and print AutomationProperties.LiveSetting for
        // all TextBlock elements that have a non-Off live-setting, so the a11y-dump
        // verifies that LiveRegion attached behavior wired the setting correctly.
        sb.AppendLine();
        sb.AppendLine("=== Live-region TextBlocks (AutomationProperties.LiveSetting != Off) ===");
        AppendLiveRegionAttrs(window, sb);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString());
    }

    private static void Walk(AutomationPeer peer, int depth, StringBuilder sb)
    {
        var indent = new string(' ', depth * 2);
        var type = peer.GetAutomationControlType();
        var name = peer.GetName() ?? "";
        var id = peer.GetAutomationId() ?? "";
        var patterns = DescribePatterns(peer);
        sb.AppendLine($"{indent}{type} | name='{name}' | id='{id}'{patterns}");

        var children = peer.GetChildren();
        if (children == null) return;
        foreach (var child in children)
            Walk(child, depth + 1, sb);
    }

    private static string DescribePatterns(AutomationPeer peer)
    {
        var found = new List<string>();
        if (peer.GetPattern(PatternInterface.RangeValue) is IRangeValueProvider rv)
            found.Add($"RangeValue(min={rv.Minimum},max={rv.Maximum},val={rv.Value})");
        if (peer.GetPattern(PatternInterface.Invoke) is not null) found.Add("Invoke");
        if (peer.GetPattern(PatternInterface.Toggle) is not null) found.Add("Toggle");
        if (peer.GetPattern(PatternInterface.SelectionItem) is not null) found.Add("SelectionItem");
        return found.Count == 0 ? "" : " | " + string.Join(",", found);
    }

    /// <summary>
    /// Walks the WPF visual tree from <paramref name="root"/> and, for each
    /// <see cref="TextBlock"/> whose <c>AutomationProperties.LiveSetting</c> is not
    /// <see cref="AutomationLiveSetting.Off"/>, appends a line showing its text and live-setting.
    /// Used by the D2 a11y-dump to verify that <c>LiveRegion</c> attached behavior is wired.
    /// </summary>
    private static void AppendLiveRegionAttrs(DependencyObject root, StringBuilder sb)
    {
        if (root is TextBlock tb)
        {
            var liveSetting = AutomationProperties.GetLiveSetting(tb);
            if (liveSetting != AutomationLiveSetting.Off)
            {
                var text = tb.Text ?? "";
                sb.AppendLine($"  TextBlock text='{text}' | LiveSetting={liveSetting}");
            }
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            AppendLiveRegionAttrs(child, sb);
        }
    }

    /// <summary>
    /// Walks the WPF visual tree from <paramref name="root"/> and, for each
    /// <see cref="ItemsControl"/> (including <see cref="ListBox"/>), appends a line
    /// showing its <c>KeyboardNavigation.TabNavigation</c>,
    /// <c>KeyboardNavigation.DirectionalNavigation</c>, and
    /// <c>TextSearch.TextPath</c> attached property values.
    /// </summary>
    private static void AppendContainerNavAttrs(DependencyObject root, StringBuilder sb)
    {
        if (root is ItemsControl ic)
        {
            var typeName = ic.GetType().Name;
            var autoName = ic is FrameworkElement fe
                ? (AutomationProperties.GetName(fe) is { Length: > 0 } n ? n : "(unnamed)")
                : "(unnamed)";
            var tabNav = KeyboardNavigation.GetTabNavigation(ic);
            var dirNav = KeyboardNavigation.GetDirectionalNavigation(ic);
            var textPath = TextSearch.GetTextPath(ic);
            sb.AppendLine($"  {typeName} name='{autoName}' | TabNavigation={tabNav} | DirectionalNavigation={dirNav} | TextPath='{textPath}'");
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            AppendContainerNavAttrs(child, sb);
        }
    }
}
