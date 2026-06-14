using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace VideoShelf.App.Harness;

public static class A11yTreeDumper
{
    public static void Dump(Window window, string path)
    {
        var sb = new StringBuilder();
        var peer = UIElementAutomationPeer.CreatePeerForElement(window)
                   ?? new WindowAutomationPeer(window);
        Walk(peer, 0, sb);
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
}
