namespace VideoShelf.App.Scale;

public static class VisualNodeCounter
{
    /// <summary>Counts a node + all descendants via a caller-supplied child accessor.
    /// The harness passes a VisualTreeHelper-backed accessor; tests pass a fake.</summary>
    public static int Count<T>(T root, Func<T, IEnumerable<T>> children)
    {
        int n = 1;
        foreach (var c in children(root)) n += Count(c, children);
        return n;
    }
}
