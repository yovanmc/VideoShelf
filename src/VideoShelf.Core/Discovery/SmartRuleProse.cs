using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VideoShelf.Core.Discovery;

/// <summary>
/// Renders a <see cref="SmartViewDefinition"/> as a human-readable plain-English summary.
/// All methods are pure / side-effect-free.
/// </summary>
public static class SmartRuleProse
{
    /// <summary>
    /// Returns a plain-English description of <paramref name="def"/>.
    /// Example: "All of: tagged anime, unwatched, longer than 30 min"
    /// </summary>
    /// <param name="def">The smart view definition to describe.</param>
    /// <param name="creatorNames">
    /// Optional map of section id → display name.
    /// When null or when an id is missing, falls back to "creator #&lt;id&gt;".
    /// </param>
    public static string Describe(
        SmartViewDefinition def,
        IReadOnlyDictionary<long, string>? creatorNames = null)
    {
        if (def.Rules.Count == 0)
            return "(no rules)";

        var prefix = def.Match.ToLowerInvariant() switch
        {
            "all" => "All of:",
            "any" => "Any of:",
            _     => $"{def.Match} of:",   // graceful fallback for unknown match tokens
        };

        var clauses = def.Rules
            .Select(r => DescribeRule(r, creatorNames))
            .ToList();

        return $"{prefix} {string.Join(", ", clauses)}";
    }

    // ── Per-rule renderer ────────────────────────────────────────────────────

    private static string DescribeRule(
        SmartRule rule,
        IReadOnlyDictionary<long, string>? creatorNames)
    {
        return rule.Field switch
        {
            "tag"       => DescribeTagRule(rule),
            "creator"   => DescribeCreatorRule(rule, creatorNames),
            "watched"   => DescribeWatchedRule(rule),
            "dateAdded" => DescribeDateAddedRule(rule),
            "duration"  => DescribeDurationRule(rule),
            // Unknown field/op — raw token fallback, never throws
            _           => $"{rule.Field} {rule.Op} {rule.Value}",
        };
    }

    private static string DescribeTagRule(SmartRule rule) =>
        rule.Op switch
        {
            "is"    => $"tagged {rule.Value}",
            "isNot" => $"not tagged {rule.Value}",
            _       => $"{rule.Field} {rule.Op} {rule.Value}",
        };

    private static string DescribeCreatorRule(
        SmartRule rule, IReadOnlyDictionary<long, string>? creatorNames)
    {
        string creatorLabel;
        if (long.TryParse(rule.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            creatorLabel = creatorNames != null && creatorNames.TryGetValue(id, out var name)
                ? name
                : $"creator #{id}";
        }
        else
        {
            // Value wasn't a valid long — use raw value as label
            creatorLabel = rule.Value;
        }

        return rule.Op switch
        {
            "is"    => $"by {creatorLabel}",
            "isNot" => $"not by {creatorLabel}",
            _       => $"{rule.Field} {rule.Op} {rule.Value}",
        };
    }

    private static string DescribeWatchedRule(SmartRule rule) =>
        rule.Op switch
        {
            "is" => rule.Value.ToLowerInvariant() switch
            {
                "true"  => "watched",
                "false" => "unwatched",
                _       => $"watched {rule.Value}",
            },
            _ => $"{rule.Field} {rule.Op} {rule.Value}",
        };

    private static string DescribeDateAddedRule(SmartRule rule)
    {
        if (!int.TryParse(rule.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
            return $"{rule.Field} {rule.Op} {rule.Value}";

        return rule.Op switch
        {
            "withinDays" => $"added in the last {days} days",
            "beforeDays" => $"added more than {days} days ago",
            _            => $"{rule.Field} {rule.Op} {rule.Value}",
        };
    }

    private static string DescribeDurationRule(SmartRule rule)
    {
        if (!long.TryParse(rule.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return $"{rule.Field} {rule.Op} {rule.Value}";

        var human = HumanDuration(seconds);
        return rule.Op switch
        {
            "gt" => $"longer than {human}",
            "lt" => $"shorter than {human}",
            _    => $"{rule.Field} {rule.Op} {rule.Value}",
        };
    }

    // ── Duration helper ──────────────────────────────────────────────────────

    /// <summary>
    /// Formats a duration in seconds as a compact human string.
    /// Examples: 45 → "45s", 90 → "1 min", 3600 → "1h 0m", 5400 → "1h 30m", 1800 → "30 min"
    /// </summary>
    internal static string HumanDuration(long seconds)
    {
        if (seconds < 60)
            return $"{seconds}s";

        var totalMinutes = seconds / 60;
        if (totalMinutes < 60)
            return $"{totalMinutes} min";

        var hours = totalMinutes / 60;
        var mins  = totalMinutes % 60;
        return mins == 0 ? $"{hours}h" : $"{hours}h {mins}m";
    }
}
