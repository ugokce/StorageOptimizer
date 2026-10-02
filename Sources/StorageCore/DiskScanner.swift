import Foundation

public struct ScanOptions: Sendable {
    public var sizeMode: SizeMode
    /// Files at or above this size become nodes in the tree; smaller ones are only summed per folder.
    public var detailThreshold: Int64
    /// How many of the largest files and packages to keep overall.
    public var topFileCount: Int
    /// How many of the largest files and packages to keep per category.
    public var topFilesPerCategory: Int
    /// Absolute paths that are not scanned.
    public var skipPaths: Set<String>
    /// Folder depth up to which subfolders are scanned in parallel.
    public var parallelDepth: Int

    public init(
        sizeMode: SizeMode = .allocated,
        detailThreshold: Int64 = 1_000_000,
        topFileCount: Int = 2_000,
        topFilesPerCategory: Int = 300,
        skipPaths: Set<String> = [],
        parallelDepth: Int = 2
    ) {
        self.sizeMode = sizeMode
        self.detailThreshold = detailThreshold
        self.topFileCount = topFileCount
        self.topFilesPerCategory = topFilesPerCategory
        self.skipPaths = skipPaths
        self.parallelDepth = parallelDepth
    }

    /// Paths to skip when scanning `/`: other volumes, device files and system mirrors
    /// that would otherwise be counted twice.
    public static let entireDiskSkipPaths: Set<String> = [
        "/dev", "/net", "/home", "/Volumes", "/System/Volumes", "/private/var/vm", "/cores",
    ]
}

public enum ScanError: LocalizedError {
    case notADirectory(String)

    public var errorDescription: String? {
        switch self {
        case .notADirectory(let path): "\(path) is not a folder."
        }
    }
}

/// Thread-safe progress counters that the UI polls while a scan runs.
public final class ScanProgress: @unchecked Sendable {
    public struct Snapshot: Equatable, Sendable {
        public var filesScanned: Int
        public var bytesScanned: Int64
        public var currentPath: String

        public init(filesScanned: Int = 0, bytesScanned: Int64 = 0, currentPath: String = "") {
            self.filesScanned = filesScanned
            self.bytesScanned = bytesScanned
            self.currentPath = currentPath
        }
    }

    private let lock = NSLock()
    private var value = Snapshot()

    public init() {}

    public func snapshot() -> Snapshot {
        lock.lock()
        defer { lock.unlock() }
        return value
    }

    func record(files: Int, bytes: Int64, currentPath: String?) {
        lock.lock()
        defer { lock.unlock() }
        value.filesScanned += files
        value.bytesScanned += bytes
        if let currentPath {
            value.currentPath = currentPath
        }
    }
}

/// Walks a folder tree and builds a `ScanResult`.
public struct DiskScanner: Sendable {
    public let options: ScanOptions
    private let skipPaths: Set<String>

    /// Folders deeper than this are measured without building nodes, to bound recursion.
    private static let maxTreeDepth = 200

    public init(options: ScanOptions = ScanOptions()) {
        self.options = options
        skipPaths = Set(options.skipPaths.map { PathUtilities.canonicalPath($0) })
    }

    public func scan(root: URL, progress: ScanProgress? = nil) async throws -> ScanResult {
        try Task.checkCancellation()
        let start = Date()
        let rootURL = root.standardizedFileURL
        let values = try rootURL.resourceValues(forKeys: FileSizing.resourceKeySet)
        guard values.isDirectory == true else {
            throw ScanError.notADirectory(rootURL.path)
        }

        let rootNode = FileNode(
            url: rootURL,
            name: FileManager.default.displayName(atPath: rootURL.path),
            isDirectory: true,
            isPackage: false,
            category: .other,
            modificationDate: values.contentModificationDate,
            accessDate: values.contentAccessDate
        )
        var accumulator = try await scanDirectory(rootNode, context: ScanContext(), depth: 0, progress: progress)
        rootNode.children.sort { $0.size > $1.size }
        accumulator.flushProgress(to: progress, directory: rootURL)
        return ScanResult(root: rootNode, accumulator: accumulator, duration: Date().timeIntervalSince(start))
    }

    // MARK: - Traversal

    /// Scans `node`'s contents. Subfolders above `parallelDepth` are scanned concurrently.
    private func scanDirectory(
        _ node: FileNode,
        context: ScanContext,
        depth: Int,
        progress: ScanProgress?
    ) async throws -> ScanAccumulator {
        var accumulator = ScanAccumulator(options: options)
        if depth >= options.parallelDepth {
            try scanDirectorySync(node, context: context, depth: depth, accumulator: &accumulator, progress: progress)
            accumulator.flushProgress(to: progress, directory: node.url)
            return accumulator
        }

        try Task.checkCancellation()
        var subdirectories: [PendingDirectory] = []
        for entry in listChildren(of: node.url, accumulator: &accumulator) {
            switch entry {
            case let .file(url, values):
                addFile(url: url, values: values, to: node, context: context, accumulator: &accumulator, progress: progress)
            case let .directory(url, values, isPackage):
                subdirectories.append(makePendingDirectory(url: url, values: values, isPackage: isPackage, parentContext: context))
            }
        }

        let scanner = self
        let scanned = try await withThrowingTaskGroup(
            of: (PendingDirectory, ScanAccumulator).self,
            returning: [(PendingDirectory, ScanAccumulator)].self
        ) { group in
            for pending in subdirectories {
                group.addTask {
                    let childAccumulator = try await scanner.scanDirectory(
                        pending.node,
                        context: pending.context,
                        depth: depth + 1,
                        progress: progress
                    )
                    return (pending, childAccumulator)
                }
            }
            var results: [(PendingDirectory, ScanAccumulator)] = []
            for try await result in group {
                results.append(result)
            }
            return results
        }

        for (pending, childAccumulator) in scanned {
            accumulator.merge(childAccumulator)
            complete(pending, parent: node, accumulator: &accumulator)
        }
        accumulator.flushProgress(to: progress, directory: node.url)
        return accumulator
    }

    private func scanDirectorySync(
        _ node: FileNode,
        context: ScanContext,
        depth: Int,
        accumulator: inout ScanAccumulator,
        progress: ScanProgress?
    ) throws {
        if Task.isCancelled { throw CancellationError() }

        if depth > Self.maxTreeDepth {
            // Extremely deep hierarchies are measured iteratively instead of being expanded.
            node.size = DirectorySizer.size(of: node.url, mode: options.sizeMode)
            return
        }

        for entry in listChildren(of: node.url, accumulator: &accumulator) {
            switch entry {
            case let .file(url, values):
                addFile(url: url, values: values, to: node, context: context, accumulator: &accumulator, progress: progress)
            case let .directory(url, values, isPackage):
                let pending = makePendingDirectory(url: url, values: values, isPackage: isPackage, parentContext: context)
                try scanDirectorySync(pending.node, context: pending.context, depth: depth + 1, accumulator: &accumulator, progress: progress)
                complete(pending, parent: node, accumulator: &accumulator)
            }
        }
    }

    private func listChildren(of directory: URL, accumulator: inout ScanAccumulator) -> [ChildEntry] {
        let urls: [URL]
        do {
            urls = try FileManager.default.contentsOfDirectory(
                at: directory,
                includingPropertiesForKeys: FileSizing.resourceKeys,
                options: []
            )
        } catch {
            accumulator.inaccessibleCount += 1
            return []
        }

        var entries: [ChildEntry] = []
        entries.reserveCapacity(urls.count)
        for url in urls {
            guard let values = try? url.resourceValues(forKeys: FileSizing.resourceKeySet) else {
                accumulator.inaccessibleCount += 1
                continue
            }
            if values.isSymbolicLink == true { continue }

            if values.isDirectory == true {
                if !skipPaths.isEmpty && skipPaths.contains(PathUtilities.canonicalPath(url)) { continue }
                var extraKeys: Set<URLResourceKey> = [.isVolumeKey]
                if !url.pathExtension.isEmpty { extraKeys.insert(.isPackageKey) }
                let extra = try? url.resourceValues(forKeys: extraKeys)
                // Never descend into other mounted volumes.
                if extra?.isVolume == true { continue }
                entries.append(.directory(url, values, isPackage: extra?.isPackage == true))
            } else if values.isRegularFile == true {
                entries.append(.file(url, values))
            }
        }
        return entries
    }

    private func addFile(
        url: URL,
        values: URLResourceValues,
        to parent: FileNode,
        context: ScanContext,
        accumulator: inout ScanAccumulator,
        progress: ScanProgress?
    ) {
        let size = FileSizing.size(from: values, mode: options.sizeMode)
        parent.size += size
        parent.fileCount += 1
        accumulator.noteFile(bytes: size, directory: parent.url, progress: progress)

        let fileExtension = url.pathExtension.lowercased()
        let category = accumulator.resolver.category(forExtension: fileExtension)

        if size >= options.detailThreshold {
            let node = FileNode(
                url: url,
                name: url.lastPathComponent,
                isDirectory: false,
                isPackage: false,
                category: category,
                size: size,
                fileCount: 1,
                modificationDate: values.contentModificationDate,
                accessDate: values.contentAccessDate
            )
            node.parent = parent
            parent.children.append(node)
        } else {
            parent.smallFilesSize += size
            parent.smallFilesCount += 1
        }

        // Files inside packages are reported as part of the package itself.
        if !context.insidePackage {
            accumulator.recordItem(
                url: url,
                size: size,
                category: category,
                fileExtension: fileExtension,
                isPackage: false,
                modificationDate: values.contentModificationDate,
                accessDate: values.contentAccessDate
            )
        }
    }

    private func makePendingDirectory(
        url: URL,
        values: URLResourceValues,
        isPackage: Bool,
        parentContext: ScanContext
    ) -> PendingDirectory {
        let name = url.lastPathComponent
        let category: FileCategory = isPackage ? FileCategory.category(forExtension: url.pathExtension) : .other
        var artifactKind: ArtifactKind?
        if !parentContext.insidePackage && !parentContext.insideArtifact {
            artifactKind = ArtifactKind.detect(directory: url, name: name)
        }
        let node = FileNode(
            url: url,
            name: name,
            isDirectory: true,
            isPackage: isPackage,
            category: category,
            modificationDate: values.contentModificationDate,
            accessDate: values.contentAccessDate
        )
        return PendingDirectory(
            node: node,
            context: ScanContext(
                insidePackage: parentContext.insidePackage || isPackage,
                insideArtifact: parentContext.insideArtifact || artifactKind != nil
            ),
            artifactKind: artifactKind,
            isPackageRoot: isPackage && !parentContext.insidePackage
        )
    }

    /// Attaches a fully scanned folder to its parent and records package and artifact information.
    private func complete(_ pending: PendingDirectory, parent: FileNode, accumulator: inout ScanAccumulator) {
        let child = pending.node
        child.children.sort { $0.size > $1.size }
        child.parent = parent
        parent.children.append(child)
        parent.size += child.size
        parent.fileCount += child.fileCount

        if pending.isPackageRoot {
            accumulator.recordItem(
                url: child.url,
                size: child.size,
                category: child.category,
                fileExtension: child.url.pathExtension.lowercased(),
                isPackage: true,
                modificationDate: child.modificationDate,
                accessDate: child.accessDate
            )
        }
        if let kind = pending.artifactKind {
            accumulator.artifacts.append(ArtifactDirectory(url: child.url, kind: kind, size: child.size))
        }
    }
}

// MARK: - Scan state

struct ScanContext: Sendable {
    var insidePackage = false
    var insideArtifact = false
}

enum ChildEntry {
    case file(URL, URLResourceValues)
    case directory(URL, URLResourceValues, isPackage: Bool)
}

struct PendingDirectory: @unchecked Sendable {
    let node: FileNode
    let context: ScanContext
    let artifactKind: ArtifactKind?
    let isPackageRoot: Bool
}

/// Statistics gathered by one scan task; merged when tasks finish.
struct ScanAccumulator: @unchecked Sendable {
    let options: ScanOptions
    var largest: TopFilesCollector
    var categoryStats: [FileCategory: UsageStats] = [:]
    var categoryTop: [FileCategory: TopFilesCollector] = [:]
    var extensionStats: [String: (category: FileCategory, stats: UsageStats)] = [:]
    var artifacts: [ArtifactDirectory] = []
    var inaccessibleCount = 0
    var resolver = CategoryResolver()
    private var pendingFiles = 0
    private var pendingBytes: Int64 = 0
    private static let progressBatchSize = 512

    init(options: ScanOptions) {
        self.options = options
        largest = TopFilesCollector(capacity: options.topFileCount)
    }

    mutating func recordItem(
        url: URL,
        size: Int64,
        category: FileCategory,
        fileExtension: String,
        isPackage: Bool,
        modificationDate: Date?,
        accessDate: Date?
    ) {
        categoryStats[category, default: UsageStats()].add(size)
        extensionStats[fileExtension, default: (category: category, stats: UsageStats())].stats.add(size)

        let keepOverall = largest.accepts(size)
        let keepInCategory = categoryTop[category]?.accepts(size) ?? (options.topFilesPerCategory > 0)
        guard keepOverall || keepInCategory else { return }

        let entry = FileEntry(
            url: url,
            size: size,
            category: category,
            isPackage: isPackage,
            modificationDate: modificationDate,
            accessDate: accessDate
        )
        if keepOverall {
            largest.insert(entry)
        }
        if keepInCategory {
            categoryTop[category, default: TopFilesCollector(capacity: options.topFilesPerCategory)].insert(entry)
        }
    }

    mutating func merge(_ other: ScanAccumulator) {
        largest.merge(other.largest)
        for (category, stats) in other.categoryStats {
            categoryStats[category, default: UsageStats()].merge(stats)
        }
        for (category, top) in other.categoryTop {
            categoryTop[category, default: TopFilesCollector(capacity: options.topFilesPerCategory)].merge(top)
        }
        for (fileExtension, value) in other.extensionStats {
            extensionStats[fileExtension, default: (category: value.category, stats: UsageStats())].stats.merge(value.stats)
        }
        artifacts.append(contentsOf: other.artifacts)
        inaccessibleCount += other.inaccessibleCount
    }

    mutating func noteFile(bytes: Int64, directory: URL, progress: ScanProgress?) {
        pendingFiles += 1
        pendingBytes += bytes
        if pendingFiles >= Self.progressBatchSize {
            flushProgress(to: progress, directory: directory)
        }
    }

    mutating func flushProgress(to progress: ScanProgress?, directory: URL) {
        guard pendingFiles > 0 || pendingBytes > 0 else { return }
        progress?.record(files: pendingFiles, bytes: pendingBytes, currentPath: directory.path)
        pendingFiles = 0
        pendingBytes = 0
    }
}
