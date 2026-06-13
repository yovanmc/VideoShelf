using System;
using System.Collections.Generic;
using System.Globalization;
using VideoShelf.Core.Storage;

namespace VideoShelf.Core.Discovery;

/// <summary>
/// Compiles a <see cref="SmartViewDefinition"/> into a parameterized SQL WHERE fragment.
///
/// The caller prepends "v.missing = 0" and the base FROM/JOIN clause:
///   FROM videos v
///   JOIN series s ON s.id = v.series_id
///   JOIN sections sec ON sec.id = s.section_id
///
/// Params are named $p0, $p1, … (one per rule, sequentially assigned).
/// A single rule may reference its $pN placeholder multiple times (e.g. tag EXISTS union).
///
/// added_at cutoff format: DateTimeOffset.ToString("o") — ISO 8601 round-trip, matching
/// LibraryRepository.UpsertVideo which writes: DateTimeOffset.UtcNow.ToString("o").
/// Lexical string comparison in SQLite is valid because "o" produces a fixed-width,
/// zero-padded UTC string (e.g. "2026-06-13T00:00:00.0000000+00:00").
/// </summary>
public static class SmartViewSqlBuilder
{
    private const string TagExistsTemplate =
        "EXISTS (" +
        "SELECT 1 FROM video_tags vt WHERE vt.video_id = v.id AND vt.tag = {0} " +
        "UNION SELECT 1 FROM series_tags st WHERE st.series_id = v.series_id AND st.tag = {0} " +
        "UNION SELECT 1 FROM section_tags sect WHERE sect.section_id = s.section_id AND sect.tag = {0}" +
        ")";

    public static (string Where, IReadOnlyList<(string Name, object Value)> Params) Build(
        SmartViewDefinition def, DateTimeOffset now)
    {
        if (def.Rules.Count == 0)
            return (string.Empty, Array.Empty<(string, object)>());

        var joinOp = def.Match.ToLowerInvariant() switch
        {
            "all" => " AND ",
            "any" => " OR ",
            _ => throw new ArgumentException($"Unknown Match value: '{def.Match}'. Expected \"all\" or \"any\".", nameof(def))
        };

        var fragments = new List<string>(def.Rules.Count);
        var paramList = new List<(string Name, object Value)>(def.Rules.Count);

        for (int i = 0; i < def.Rules.Count; i++)
        {
            var rule = def.Rules[i];
            var paramName = $"$p{i}";
            var (fragment, paramValue) = BuildRule(rule, paramName, now);
            fragments.Add(fragment);
            paramList.Add((paramName, paramValue));
        }

        var combined = string.Join(joinOp, fragments);
        return ($"({combined})", paramList);
    }

    private static (string Fragment, object ParamValue) BuildRule(
        SmartRule rule, string paramName, DateTimeOffset now)
    {
        return rule.Field switch
        {
            "tag" => BuildTagRule(rule, paramName),
            "creator" => BuildCreatorRule(rule, paramName),
            "watched" => BuildWatchedRule(rule, paramName),
            "dateAdded" => BuildDateAddedRule(rule, paramName, now),
            "duration" => BuildDurationRule(rule, paramName),
            _ => throw new ArgumentException($"Unknown field: '{rule.Field}'.", nameof(rule))
        };
    }

    private static (string Fragment, object ParamValue) BuildTagRule(SmartRule rule, string paramName)
    {
        var normalizedTag = TagRepository.Normalize(rule.Value);
        var existsFrag = string.Format(CultureInfo.InvariantCulture, TagExistsTemplate, paramName);

        return rule.Op switch
        {
            "is"    => (existsFrag, normalizedTag),
            "isNot" => ($"NOT ({existsFrag})", (object)normalizedTag),
            _ => throw new ArgumentException($"Unknown op '{rule.Op}' for field 'tag'.", nameof(rule))
        };
    }

    private static (string Fragment, object ParamValue) BuildCreatorRule(SmartRule rule, string paramName)
    {
        var sectionId = long.Parse(rule.Value, CultureInfo.InvariantCulture);
        return rule.Op switch
        {
            "is"    => ($"s.section_id = {paramName}", (object)sectionId),
            "isNot" => ($"s.section_id <> {paramName}", (object)sectionId),
            _ => throw new ArgumentException($"Unknown op '{rule.Op}' for field 'creator'.", nameof(rule))
        };
    }

    private static (string Fragment, object ParamValue) BuildWatchedRule(SmartRule rule, string paramName)
    {
        if (rule.Op != "is")
            throw new ArgumentException($"Unknown op '{rule.Op}' for field 'watched'.", nameof(rule));

        var paramValue = rule.Value == "true" ? 1L : 0L;
        return ($"v.watched = {paramName}", paramValue);
    }

    private static (string Fragment, object ParamValue) BuildDateAddedRule(
        SmartRule rule, string paramName, DateTimeOffset now)
    {
        int days = int.Parse(rule.Value, CultureInfo.InvariantCulture);
        // Format matches LibraryRepository.UpsertVideo: DateTimeOffset.UtcNow.ToString("o")
        // e.g. "2026-06-13T00:00:00.0000000+00:00" — lexical comparison is valid in SQLite
        // because "o" produces a fixed-width, sortable UTC string.
        var cutoff = now.AddDays(-days).ToString("o", CultureInfo.InvariantCulture);

        return rule.Op switch
        {
            "withinDays" => ($"v.added_at >= {paramName}", (object)cutoff),
            "beforeDays" => ($"v.added_at < {paramName}", (object)cutoff),
            _ => throw new ArgumentException($"Unknown op '{rule.Op}' for field 'dateAdded'.", nameof(rule))
        };
    }

    private static (string Fragment, object ParamValue) BuildDurationRule(SmartRule rule, string paramName)
    {
        var seconds = long.Parse(rule.Value, CultureInfo.InvariantCulture);
        return rule.Op switch
        {
            "gt" => ($"(v.duration IS NOT NULL AND v.duration > {paramName})", (object)seconds),
            "lt" => ($"(v.duration IS NOT NULL AND v.duration < {paramName})", (object)seconds),
            _ => throw new ArgumentException($"Unknown op '{rule.Op}' for field 'duration'.", nameof(rule))
        };
    }
}
