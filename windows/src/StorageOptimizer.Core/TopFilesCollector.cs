namespace StorageOptimizer.Core;

/// <summary>Keeps the <see cref="Capacity"/> largest entries seen so far (a bounded min-heap).</summary>
public sealed class TopFilesCollector(int capacity)
{
    private readonly PriorityQueue<FileEntry, long> _heap = new();

    public int Capacity { get; } = Math.Max(0, capacity);
    public int Count => _heap.Count;

    /// <summary>Whether an entry of <paramref name="size"/> bytes would currently be kept.</summary>
    public bool Accepts(long size)
    {
        if (Capacity == 0)
        {
            return false;
        }
        return _heap.Count < Capacity || (_heap.TryPeek(out _, out var smallest) && size > smallest);
    }

    public void Add(FileEntry entry)
    {
        if (Capacity == 0)
        {
            return;
        }
        if (_heap.Count < Capacity)
        {
            _heap.Enqueue(entry, entry.Size);
        }
        else if (_heap.TryPeek(out _, out var smallest) && entry.Size > smallest)
        {
            _heap.EnqueueDequeue(entry, entry.Size);
        }
    }

    public void Merge(TopFilesCollector other)
    {
        foreach (var (entry, _) in other._heap.UnorderedItems)
        {
            Add(entry);
        }
    }

    /// <summary>The collected entries, largest first.</summary>
    public List<FileEntry> SortedDescending() =>
        _heap.UnorderedItems
            .Select(item => item.Element)
            .OrderByDescending(entry => entry.Size)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();
}
