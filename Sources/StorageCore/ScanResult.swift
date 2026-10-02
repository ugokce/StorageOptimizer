import Foundation

/// Size and item count of a group of files.
public struct UsageStats: Hashable, Sendable {
    public var size: Int64 = 0
    public var count: Int = 0

    public init(size: Int64 = 0, count: Int = 0) {
        self.size = size
        self.count = count
    }

    mutating func add(_ bytes: Int64) {
        size += bytes
        count += 1
    }

    mutating func remove(_ bytes: Int64) {
        size = max(0, size - bytes)
        count = max(0, count - 1)
    }

    mutating func merge(_ other: UsageStats) {
        size += other.size
        count += other.count
    }
}

public struct CategorySummary: Identifiable, Hashable, Sendable {
    public let category: FileCategory
    public let size: Int64
    public let count: Int
    public var id: FileCategory { category }
}

public struct ExtensionSummary: Identifiable, Hashable, Sendable {
    /// Lowercased extension without the dot; empty for files without one.
    public let fileExtension: String
    public let category: FileCategory
    public let size: Int64
    public let count: Int
    public var id: String { fileExtension }
    public var displayName: String { fileExtension.isEmpty ? "No extension" : "." + fileExtension }
}

/// Everything learned from scanning one folder.
public struct ScanResult: Sendable {
    public let root: FileNode
    /// The largest files and packages, largest first.
    public private(set) var largestFiles: [FileEntry]
    public private(set) var categoryStats: [FileCategory: UsageStats]
    /// The largest files and packages of each category, largest first.
    public private(set) var categoryFiles: [FileCategory: [FileEntry]]
    var extensionStats: [String: (category: FileCategory, stats: UsageStats)]
    /// Rebuildable project folders (node_modules, Pods, ...), largest first.
    public private(set) var artifactDirectories: [ArtifactDirectory]
    /// Folders that could not be read (usually missing permissions).
    public let inaccessibleCount: Int
    public let duration: TimeInterval
    public let scannedAt: Date

    init(root: FileNode, accumulator: ScanAccumulator, duration: TimeInterval) {
        self.root = root
        largestFiles = accumulator.largest.sortedDescending()
        categoryStats = accumulator.categoryStats
        categoryFiles = accumulator.categoryTop.mapValues { $0.sortedDescending() }
        extensionStats = accumulator.extensionStats
        artifactDirectories = accumulator.artifacts.sorted { $0.size > $1.size }
        inaccessibleCount = accumulator.inaccessibleCount
        self.duration = duration
        scannedAt = Date()
    }

    public var totalSize: Int64 { root.size }
    public var fileCount: Int { root.fileCount }

    /// Categories that contain data, largest first.
    public var categorySummaries: [CategorySummary] {
        categoryStats
            .filter { $0.value.size > 0 }
            .map { CategorySummary(category: $0.key, size: $0.value.size, count: $0.value.count) }
            .sorted { $0.size > $1.size }
    }

    /// File extensions, largest first, optionally limited to one category.
    public func extensionSummaries(for category: FileCategory? = nil) -> [ExtensionSummary] {
        extensionStats
            .filter { (category == nil || $0.value.category == category) && $0.value.stats.size > 0 }
            .map {
                ExtensionSummary(
                    fileExtension: $0.key,
                    category: $0.value.category,
                    size: $0.value.stats.size,
                    count: $0.value.stats.count
                )
            }
            .sorted { $0.size > $1.size }
    }

    /// Finds the node for `url`, if it is part of the tree.
    public func node(at url: URL) -> FileNode? {
        let rootPath = PathUtilities.canonicalPath(root.url)
        let target = PathUtilities.canonicalPath(url)
        if target == rootPath { return root }
        guard PathUtilities.isPath(target, inside: rootPath) else { return nil }
        let relative = rootPath == "/" ? String(target.dropFirst()) : String(target.dropFirst(rootPath.count + 1))
        var current = root
        for component in relative.split(separator: "/") {
            guard let next = current.children.first(where: { $0.name == component }) else { return nil }
            current = next
        }
        return current
    }

    /// Updates the tree and statistics after `url` was deleted or moved to the Trash.
    ///
    /// Statistics are exact for files and packages. For folders, small files inside them
    /// are only removed from the totals, not from the per-type statistics.
    public mutating func applyRemoval(of url: URL) {
        let removedPath = PathUtilities.canonicalPath(url)
        let isAffected: (URL) -> Bool = { PathUtilities.isPath(PathUtilities.canonicalPath($0), inside: removedPath) }

        if let removedNode = node(at: url), removedNode !== root {
            if !removedNode.isInsidePackage {
                subtractStatistics(of: removedNode)
            }
            removedNode.removeFromParent()
        } else if let entry = largestFiles.first(where: { isAffected($0.url) })
                    ?? categoryFiles.values.joined().first(where: { isAffected($0.url) }) {
            // A file below the detail threshold: it only exists in its folder's small-file totals.
            node(at: url.deletingLastPathComponent())?.removeSmallFile(size: entry.size)
            subtract(category: entry.category, fileExtension: entry.url.pathExtension.lowercased(), size: entry.size)
        }

        largestFiles.removeAll { isAffected($0.url) }
        for category in Array(categoryFiles.keys) {
            categoryFiles[category]?.removeAll { isAffected($0.url) }
        }
        artifactDirectories.removeAll { isAffected($0.url) }
    }

    private mutating func subtractStatistics(of node: FileNode) {
        var stack = [node]
        while let current = stack.popLast() {
            if current.isPackage || !current.isDirectory {
                subtract(category: current.category, fileExtension: current.url.pathExtension.lowercased(), size: current.size)
            } else {
                stack.append(contentsOf: current.children)
            }
        }
    }

    private mutating func subtract(category: FileCategory, fileExtension: String, size: Int64) {
        categoryStats[category]?.remove(size)
        extensionStats[fileExtension]?.stats.remove(size)
    }
}
