import Foundation

/// A folder, package or large file in the scanned tree.
///
/// To keep memory bounded only folders and files at or above the scan's detail threshold
/// become nodes; smaller files are summed into their folder's `smallFilesSize`/`smallFilesCount`.
/// Nodes are built on a background task and only mutated on the main actor afterwards.
public final class FileNode: Identifiable, @unchecked Sendable {
    public let url: URL
    public let name: String
    public let isDirectory: Bool
    /// True for bundles such as `.app` or `.photoslibrary` that Finder shows as a single file.
    public let isPackage: Bool
    public let category: FileCategory
    public let modificationDate: Date?
    public let accessDate: Date?

    public internal(set) var size: Int64
    /// Number of files at or below this node.
    public internal(set) var fileCount: Int
    /// Children sorted by size, largest first.
    public internal(set) var children: [FileNode] = []
    public internal(set) var smallFilesSize: Int64 = 0
    public internal(set) var smallFilesCount: Int = 0
    public internal(set) weak var parent: FileNode?

    public var id: ObjectIdentifier { ObjectIdentifier(self) }

    /// Whether the folder browser can open this node.
    public var canDrillDown: Bool {
        isDirectory && (!children.isEmpty || smallFilesCount > 0)
    }

    init(
        url: URL,
        name: String,
        isDirectory: Bool,
        isPackage: Bool,
        category: FileCategory,
        size: Int64 = 0,
        fileCount: Int = 0,
        modificationDate: Date?,
        accessDate: Date?
    ) {
        self.url = url
        self.name = name
        self.isDirectory = isDirectory
        self.isPackage = isPackage
        self.category = category
        self.size = size
        self.fileCount = fileCount
        self.modificationDate = modificationDate
        self.accessDate = accessDate
    }

    /// Whether any ancestor is a package (its contents are reported as part of the package).
    var isInsidePackage: Bool {
        var ancestor = parent
        while let node = ancestor {
            if node.isPackage { return true }
            ancestor = node.parent
        }
        return false
    }

    /// Adds `delta` bytes and `files` files to this node and every ancestor.
    func adjustTotals(bytes delta: Int64, files: Int) {
        var current: FileNode? = self
        while let node = current {
            node.size = max(0, node.size + delta)
            node.fileCount = max(0, node.fileCount + files)
            current = node.parent
        }
    }

    /// Detaches this node and subtracts its size from all ancestors.
    func removeFromParent() {
        guard let parent else { return }
        parent.children.removeAll { $0 === self }
        parent.adjustTotals(bytes: -size, files: -fileCount)
        self.parent = nil
    }

    /// Accounts for a small (non-node) file that was removed from this folder.
    func removeSmallFile(size removedSize: Int64) {
        smallFilesSize = max(0, smallFilesSize - removedSize)
        smallFilesCount = max(0, smallFilesCount - 1)
        adjustTotals(bytes: -removedSize, files: -1)
    }
}

/// A single file or package in a "largest items" list.
public struct FileEntry: Identifiable, Hashable, Sendable {
    public let url: URL
    public let size: Int64
    public let category: FileCategory
    public let isPackage: Bool
    public let modificationDate: Date?
    public let accessDate: Date?

    public init(
        url: URL,
        size: Int64,
        category: FileCategory,
        isPackage: Bool = false,
        modificationDate: Date? = nil,
        accessDate: Date? = nil
    ) {
        self.url = url
        self.size = size
        self.category = category
        self.isPackage = isPackage
        self.modificationDate = modificationDate
        self.accessDate = accessDate
    }

    public var id: URL { url }
    public var name: String { url.lastPathComponent }
    public var kindName: String { category.displayName }
    /// The containing folder, abbreviated with `~`.
    public var displayLocation: String { url.deletingLastPathComponent().abbreviatedPath }
    public var modificationSortDate: Date { modificationDate ?? .distantPast }
    public var accessSortDate: Date { accessDate ?? .distantPast }
}
