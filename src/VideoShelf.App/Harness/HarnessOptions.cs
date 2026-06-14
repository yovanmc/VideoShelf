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

    /// <summary>True when the app was launched by the harness (any core hook present).</summary>
    public bool IsHarness => Folder is not null || DoneSignal is not null;

    public static HarnessOptions Parse(IReadOnlyList<string> args)
    {
        string? folder = null, dataDir = null, play = null, doneSignal = null;
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
                default: break; // ignore unknown
            }
        }

        return new HarnessOptions
        {
            Folder = folder, DataDir = dataDir, View = view, Play = play,
            DoneSignal = doneSignal,
            AutoStart = autoStart, SeedDemo = seedDemo,
        };
    }
}
