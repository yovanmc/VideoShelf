using System.Collections.Generic;

namespace VideoShelf.Core.Discovery;

/// <summary>A single filter rule in a smart view definition.</summary>
public sealed record SmartRule(string Field, string Op, string Value);

/// <summary>
/// A smart view definition: a list of rules combined by Match logic.
/// Match = "all" (AND) | "any" (OR).
/// </summary>
public sealed record SmartViewDefinition(string Match, IReadOnlyList<SmartRule> Rules);
