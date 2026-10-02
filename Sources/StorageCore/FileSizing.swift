import Foundation

/// How file sizes are measured.
public enum SizeMode: Sendable {
    /// Space actually used on disk (what Finder's "on disk" value and macOS Storage report).
    case allocated
    /// The logical length of the file contents.
    case logical
}

enum FileSizing {
    static let resourceKeys: [URLResourceKey] = [
        .isDirectoryKey,
        .isRegularFileKey,
        .isSymbolicLinkKey,
        .fileSizeKey,
        .totalFileSizeKey,
        .fileAllocatedSizeKey,
        .totalFileAllocatedSizeKey,
        .contentModificationDateKey,
        .contentAccessDateKey,
    ]

    static let resourceKeySet = Set(resourceKeys)

    static func size(from values: URLResourceValues, mode: SizeMode) -> Int64 {
        switch mode {
        case .allocated:
            if let allocated = values.totalFileAllocatedSize ?? values.fileAllocatedSize {
                return Int64(allocated)
            }
            return Int64(values.totalFileSize ?? values.fileSize ?? 0)
        case .logical:
            return Int64(values.totalFileSize ?? values.fileSize ?? 0)
        }
    }
}

/// Measures folders without building a tree; used by the cache cleaner and for very deep folders.
public enum DirectorySizer {
    /// Total size of the regular files at or below `url`. Symbolic links are not followed.
    /// - Parameter excludedPaths: canonical paths (see `PathUtilities.canonicalPath`) to skip entirely.
    public static func size(of url: URL, mode: SizeMode = .allocated, excluding excludedPaths: Set<String> = []) -> Int64 {
        guard let values = try? url.resourceValues(forKeys: FileSizing.resourceKeySet) else { return 0 }
        if values.isSymbolicLink == true { return 0 }
        if values.isRegularFile == true { return FileSizing.size(from: values, mode: mode) }
        guard values.isDirectory == true else { return 0 }

        guard let enumerator = FileManager.default.enumerator(
            at: url,
            includingPropertiesForKeys: FileSizing.resourceKeys,
            options: [],
            errorHandler: { _, _ in true }
        ) else { return 0 }

        var total: Int64 = 0
        for case let item as URL in enumerator {
            if !excludedPaths.isEmpty && excludedPaths.contains(PathUtilities.canonicalPath(item)) {
                enumerator.skipDescendants()
                continue
            }
            guard let itemValues = try? item.resourceValues(forKeys: FileSizing.resourceKeySet),
                  itemValues.isRegularFile == true else { continue }
            total += FileSizing.size(from: itemValues, mode: mode)
        }
        return total
    }
}
