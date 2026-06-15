using System;
using System.Collections.Generic;

namespace VideoShelf.App.Harness;

/// <summary>
/// Parsed command-line options for the visual-verification harness.
/// Unknown args are ignored so the contract is forward-compatible.
/// </summary>
public sealed record HarnessOptions
{
    public string? Folder { get; init; }
    public string? DataDir { get; init; }
    public bool AutoStart { get; init; }
    public string View { get; init; } = "Home";
    public string? Play { get; init; }
    public string? DoneSignal { get; init; }
    public bool SeedDemo { get; init; }
    public string? StressSpec { get; init; }   // "<creators>x<biggestSeries>x<totalVideos>"
    public string? MetricsOut { get; init; }

    /// <summary>True when the app was launched by the harness (any core hook present).</summary>
    public bool IsHarness => Folder is not null || DoneSignal is not null || StressSpec is not null || MetricsOut is not null;

    /// <summary>Parses the StressSpec string "CxBxT" into (creators, biggestSeries, totalVideos).</summary>
    public (int creators, int biggest, int total) ParseStressSpec()
    {
        if (StressSpec is null) throw new InvalidOperationException("StressSpec is not set.");
        var parts = StressSpec.Split('x');
        if (parts.Length != 3) throw new FormatException($"StressSpec must be 'CxBxT', got: {StressSpec}");
        return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    public static HarnessOptions Parse(IReadOnlyList<string> args)
    {
        string? folder = null, dataDir = null, play = null, doneSignal = null, stressSpec = null, metricsOut = null;
        string view = "Home";
        bool autoStart = false, seedDemo = false;

        for (var i = 0; i < args.Count; i++)
        {
            var key = args[i].ToLowerInvariant();
            string? Next() => i + 1 < args.Count ? args[++i] : null;

            switch (key)
            {
                case "--folder": folder = Next(); break;
                case "--data-dir": dataDir = Next(); break;
                case "--view": view = Next() ?? view; break;
                case "--play": play = Next(); break;
                case "--done-signal": doneSignal = Next(); break;
                case "--autostart": autoStart = true; break;
                case "--seed-demo": seedDemo = true; break;
                case "--stress":      stressSpec = Next(); break;
                case "--metrics-out": metricsOut = Next(); break;
                default: break; // ignore unknown
            }
        }

        return new HarnessOptions
        {
            Folder = folder, DataDir = dataDir, View = view, Play = play,
            DoneSignal = doneSignal,
            AutoStart = autoStart, SeedDemo = seedDemo,
            StressSpec = stressSpec, MetricsOut = metricsOut,
        };
    }
}
