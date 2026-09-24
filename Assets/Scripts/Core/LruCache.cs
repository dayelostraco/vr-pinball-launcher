using System;
using System.Collections.Generic;

namespace VRLauncher
{
    /// <summary>
    /// A fixed-size cache that drops the least recently used entry, calling
    /// <c>onEvict</c> for every value it drops (so textures can be destroyed).
    /// </summary>
    public sealed class LruCache<TKey, TValue>
    {
        private readonly int capacity;
        private readonly Action<TKey, TValue> onEvict;
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> map;
        private readonly LinkedList<KeyValuePair<TKey, TValue>> order = new LinkedList<KeyValuePair<TKey, TValue>>();

        public LruCache(int capacity, Action<TKey, TValue> onEvict = null, IEqualityComparer<TKey> comparer = null)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
            this.onEvict = onEvict;
            map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public int Count => map.Count;

        public bool TryGet(TKey key, out TValue value)
        {
            if (map.TryGetValue(key, out var node))
            {
                order.Remove(node);
                order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default;
            return false;
        }

        public void Add(TKey key, TValue value)
        {
            if (map.TryGetValue(key, out var existing))
            {
                order.Remove(existing);
                map.Remove(key);
                onEvict?.Invoke(existing.Value.Key, existing.Value.Value);
            }

            map[key] = order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));

            while (map.Count > capacity)
            {
                var last = order.Last;
                order.RemoveLast();
                map.Remove(last.Value.Key);
                onEvict?.Invoke(last.Value.Key, last.Value.Value);
            }
        }

        /// <summary>Removes an entry without invoking <c>onEvict</c>, for when ownership moves elsewhere.</summary>
        public bool Remove(TKey key)
        {
            if (map.TryGetValue(key, out var node))
            {
                order.Remove(node);
                map.Remove(key);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            foreach (var pair in order)
            {
                onEvict?.Invoke(pair.Key, pair.Value);
            }
            order.Clear();
            map.Clear();
        }
    }
}
