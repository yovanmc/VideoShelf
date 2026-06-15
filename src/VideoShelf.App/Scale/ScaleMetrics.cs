namespace VideoShelf.App.Scale;

using System.Text.Json;

public sealed class ScaleMetrics
{
    public string View { get; set; } = "";
    public int CreatorCount { get; set; }
    public int RenderedNodeCount { get; set; }
    public long InitialRenderMs { get; set; }
    public long ManagedHeapBytes { get; set; }
    public long? ScanProbeMs { get; set; }

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    public static string ToJson(IEnumerable<ScaleMetrics> items) => JsonSerializer.Serialize(items, Opts);
    public static IReadOnlyList<ScaleMetrics> FromJson(string json) =>
        JsonSerializer.Deserialize<List<ScaleMetrics>>(json) ?? new();
}
