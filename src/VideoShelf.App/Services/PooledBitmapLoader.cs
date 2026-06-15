namespace VideoShelf.App.Services;

using System.Windows.Media;
using System.Windows.Media.Imaging;

public sealed class PooledBitmapLoader : IImageLoader
{
    private readonly int _maxEntries;
    private readonly Func<string, int, object> _decode;          // seam for tests
    private readonly LinkedList<string> _order = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, object Value)> _map = new();
    private readonly object _gate = new();

    // Production ctor.
    public PooledBitmapLoader(int maxEntries = 600)
        : this(maxEntries, DecodeFrozen) { }

    // Test ctor.
    public PooledBitmapLoader(int maxEntries, Func<string, int, object> decode)
    {
        _maxEntries = Math.Max(1, maxEntries);
        _decode = decode;
    }

    public object GetOrDecode(string path, int width)
    {
        var key = $"{path}|{width}";
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var hit))
            {
                _order.Remove(hit.Node);
                _order.AddFirst(hit.Node);
                return hit.Value;
            }
            var value = _decode(path, width);
            var node = new LinkedListNode<string>(key);
            _order.AddFirst(node);
            _map[key] = (node, value);
            while (_map.Count > _maxEntries)
            {
                var lru = _order.Last!;
                _order.RemoveLast();
                _map.Remove(lru.Value);
            }
            return value;
        }
    }

    public ImageSource? Load(string? path, int decodePixelWidth)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        try { return (ImageSource)GetOrDecode(path, decodePixelWidth); }
        catch { return null; }   // fail-safe — caller shows placeholder
    }

    private static object DecodeFrozen(string path, int width)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;      // decode now, release the file handle
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.DecodePixelWidth = width;                    // decode at display size, not full-res
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();                                    // shareable across the UI thread, no per-use copy
        return bmp;
    }
}
