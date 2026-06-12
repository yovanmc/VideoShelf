// src/VideoShelf.Core/Renaming/RenameResult.cs
using System.Collections.Generic;

namespace VideoShelf.Core.Renaming;

public sealed record RenameResult(int Renamed, int Skipped, string? ManifestPath, IReadOnlyList<string> Errors);
