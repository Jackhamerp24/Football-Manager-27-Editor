namespace Fm27Editor.App;

/// <summary>Fixed-size cache that drops the least recently used entry when full. Not thread-safe: UI thread only.</summary>
sealed class LruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> map = new();
    readonly LinkedList<(TKey Key, TValue Value)> order = new();

    public int Count => map.Count;

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (map.TryGetValue(key, out var node))
        {
            order.Remove(node);
            order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public void Add(TKey key, TValue value)
    {
        if (map.TryGetValue(key, out var existing))
        {
            order.Remove(existing);
            map.Remove(key);
        }
        var node = order.AddFirst((key, value));
        map[key] = node;
        while (map.Count > capacity)
        {
            var last = order.Last!;
            order.RemoveLast();
            map.Remove(last.Value.Key);
        }
    }

    public void Clear()
    {
        map.Clear();
        order.Clear();
    }
}
