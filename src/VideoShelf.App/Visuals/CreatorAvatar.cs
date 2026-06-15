namespace VideoShelf.App.Visuals;

/// <summary>Pure helpers for a creator's fallback avatar: initials + a deterministic hue
/// derived from the name (so the same creator always gets the same color). No WPF deps here.</summary>
public static class CreatorAvatar
{
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var words = name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        var first = char.ToUpperInvariant(words[0][0]);
        if (words.Length == 1) return first.ToString();
        var last = char.ToUpperInvariant(words[^1][0]);
        return $"{first}{last}";
    }

    /// <summary>Stable hue 0..359 from the name. Uses a stable hash (NOT string.GetHashCode,
    /// which is randomized per-process) so colors are consistent across runs.</summary>
    public static int HueDegrees(string? name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        unchecked
        {
            uint h = 2166136261u;                 // FNV-1a
            foreach (char c in name) { h ^= c; h *= 16777619u; }
            return (int)(h % 360u);
        }
    }
}
