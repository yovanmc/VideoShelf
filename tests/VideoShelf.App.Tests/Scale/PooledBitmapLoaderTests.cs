using VideoShelf.App.Services;

namespace VideoShelf.App.Tests.Scale;

public class PooledBitmapLoaderTests
{
    [Fact]
    public void Lru_caps_entries_and_evicts_least_recently_used()
    {
        int decodes = 0;
        var loader = new PooledBitmapLoader(maxEntries: 2, decode: (path, w) => { decodes++; return new object(); });

        var a1 = loader.GetOrDecode("a", 200);   // miss → decode (1)
        var b1 = loader.GetOrDecode("b", 200);   // miss → decode (2)
        var a2 = loader.GetOrDecode("a", 200);   // hit  → no decode
        Assert.Same(a1, a2);
        Assert.Equal(2, decodes);

        var c1 = loader.GetOrDecode("c", 200);   // miss → evicts LRU ("b"); decode (3)
        var b2 = loader.GetOrDecode("b", 200);   // miss again (was evicted); decode (4)
        Assert.Equal(4, decodes);
    }

    [Fact]
    public void Key_includes_decode_width()
    {
        int decodes = 0;
        var loader = new PooledBitmapLoader(maxEntries: 10, decode: (p, w) => { decodes++; return new object(); });
        loader.GetOrDecode("a", 200);
        loader.GetOrDecode("a", 400);   // different width → separate entry
        Assert.Equal(2, decodes);
    }

    [Fact]
    public void Load_returns_null_for_null_or_missing_path()
    {
        var loader = new PooledBitmapLoader(maxEntries: 10, decode: (p, w) => new object());

        // null path → null (no exception)
        Assert.Null(loader.Load(null, 200));

        // empty string → null
        Assert.Null(loader.Load(string.Empty, 200));

        // non-existent file → null
        Assert.Null(loader.Load(@"C:\does\not\exist\fake.jpg", 200));
    }
}
