using System;
using System.Collections.Generic;
using System.Globalization;

namespace VideoShelf.Core.Naming;

/// <summary>Compares strings so that embedded numbers sort numerically ("Clip 2" before "Clip 10").</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        x ??= string.Empty;
        y ??= string.Empty;
        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                int sx = ix, sy = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;
                var nx = x.AsSpan(sx, ix - sx).TrimStart('0');
                var ny = y.AsSpan(sy, iy - sy).TrimStart('0');
                if (nx.Length != ny.Length) return nx.Length - ny.Length;
                var cmp = nx.CompareTo(ny, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
            }
            else
            {
                var cmp = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (cmp != 0) return cmp;
                ix++; iy++;
            }
        }
        return (x.Length - ix) - (y.Length - iy);
    }
}
