namespace DynamicIsland.Core.Media;

/// <summary>Parça anahtarına göre küçük LRU kapak önbelleği (kodlanmış bayt olarak).</summary>
internal sealed class ArtworkCache
{
    private readonly int _capacity;
    private readonly LinkedList<MediaArtwork> _order = new();
    private readonly Dictionary<string, LinkedListNode<MediaArtwork>> _map = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ArtworkCache(int capacity) => _capacity = Math.Max(1, capacity);

    public MediaArtwork? Get(string key)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                return null;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value;
        }
    }

    public void Put(MediaArtwork artwork)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(artwork.TrackKey, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(artwork.TrackKey);
            }

            var node = _order.AddFirst(artwork);
            _map[artwork.TrackKey] = node;
            while (_map.Count > _capacity && _order.Last is { } last)
            {
                _order.RemoveLast();
                _map.Remove(last.Value.TrackKey);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _order.Clear();
            _map.Clear();
        }
    }
}
