using System.Collections;

namespace Dorc.Core
{
    public static class EnumerableExtensions
    {
        public static IEnumerable<IGrouping<TKey, TSource>> Batch<TSource, TKey>(this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector)
        {
            var hasKey = false;
            var currentKey = default(TKey);

            var currentBatch = new List<TSource>();

            foreach (var item in source)
            {
                var key = keySelector(item);

                if (!Equals(key, currentKey))
                {
                    if (hasKey) yield return new BatchGroup<TKey, TSource>(currentKey!, currentBatch);

                    hasKey = true;
                    currentKey = key;
                    currentBatch.Clear();
                }

                currentBatch.Add(item);
            }

            if (hasKey && currentBatch.Count > 0) yield return new BatchGroup<TKey, TSource>(currentKey!, currentBatch);
        }
    }

    public class BatchGroup<TKey, TSource> : IGrouping<TKey, TSource>
    {
        private readonly IEnumerable<TSource> _items;

        public BatchGroup(TKey? key, IEnumerable<TSource> items)
        {
            Key = key!;
            _items = items;
        }

        public IEnumerator<TSource> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public TKey Key { get; }
    }
}