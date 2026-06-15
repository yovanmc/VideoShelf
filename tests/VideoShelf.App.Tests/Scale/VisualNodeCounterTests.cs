using VideoShelf.App.Scale;

namespace VideoShelf.App.Tests.Scale;

public class VisualNodeCounterTests
{
    [Fact]
    public void Counts_nodes_under_a_named_subtree()
    {
        // Fake tree: root → [a → [a1,a2], b]
        var tree = new FakeNode("root", new FakeNode("a", new FakeNode("a1"), new FakeNode("a2")), new FakeNode("b"));
        int count = VisualNodeCounter.Count(tree, n => n.Children);
        Assert.Equal(5, count);
    }

    private sealed record FakeNode(string Name, params FakeNode[] Children);
}
