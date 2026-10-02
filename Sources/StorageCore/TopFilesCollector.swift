import Foundation

/// Keeps the `capacity` largest entries seen so far using a bounded min-heap.
public struct TopFilesCollector: Sendable {
    public let capacity: Int
    private var heap: [FileEntry] = []

    public init(capacity: Int) {
        self.capacity = max(0, capacity)
    }

    public var count: Int { heap.count }

    /// Whether an entry of `size` bytes would currently be kept.
    public func accepts(_ size: Int64) -> Bool {
        guard capacity > 0 else { return false }
        return heap.count < capacity || size > heap[0].size
    }

    public mutating func insert(_ entry: FileEntry) {
        guard capacity > 0 else { return }
        if heap.count < capacity {
            heap.append(entry)
            siftUp(from: heap.count - 1)
        } else if entry.size > heap[0].size {
            heap[0] = entry
            siftDown(from: 0)
        }
    }

    public mutating func merge(_ other: TopFilesCollector) {
        for entry in other.heap {
            insert(entry)
        }
    }

    /// The collected entries, largest first.
    public func sortedDescending() -> [FileEntry] {
        heap.sorted { lhs, rhs in
            lhs.size != rhs.size ? lhs.size > rhs.size : lhs.url.path < rhs.url.path
        }
    }

    private mutating func siftUp(from index: Int) {
        var child = index
        while child > 0 {
            let parent = (child - 1) / 2
            guard heap[child].size < heap[parent].size else { return }
            heap.swapAt(child, parent)
            child = parent
        }
    }

    private mutating func siftDown(from index: Int) {
        var parent = index
        let count = heap.count
        while true {
            let left = 2 * parent + 1
            let right = left + 1
            var smallest = parent
            if left < count && heap[left].size < heap[smallest].size { smallest = left }
            if right < count && heap[right].size < heap[smallest].size { smallest = right }
            if smallest == parent { return }
            heap.swapAt(parent, smallest)
            parent = smallest
        }
    }
}
