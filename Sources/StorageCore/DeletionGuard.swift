import Foundation

public enum DeletionError: LocalizedError, Equatable {
    case protectedLocation(String)
    case outsideAllowedLocations(String)

    public var errorDescription: String? {
        switch self {
        case .protectedLocation(let path):
            "\(path) is a protected location and can't be deleted."
        case .outsideAllowedLocations(let path):
            "\(path) is outside your home folder, Applications and external drives, so it won't be deleted."
        }
    }
}

/// Decides whether an item may be deleted or moved to the Trash.
///
/// Only items inside the home folder, `/Applications`, `/Users/Shared` or an external volume
/// are allowed, and never the standard folders themselves (Documents, Library, Caches, ...).
public struct DeletionGuard: Sendable {
    public let homeDirectory: URL
    private let homePath: String
    private let protectedPaths: Set<String>
    private let protectedSubtrees: [String]

    private static let protectedHomeFolders = [
        "Desktop", "Documents", "Downloads", "Library", "Movies", "Music", "Pictures", "Public",
        "Applications", ".Trash",
        "Library/Caches", "Library/Logs", "Library/Application Support", "Library/Containers",
        "Library/Group Containers", "Library/Developer", "Library/Developer/Xcode",
        "Library/Mobile Documents", "Library/CloudStorage", "Library/Preferences", "Library/Mail",
    ]

    private static let allowedExternalRoots = ["/Applications", "/Users/Shared", "/Volumes"]

    public init(homeDirectory: URL = FileManager.default.homeDirectoryForCurrentUser) {
        self.homeDirectory = homeDirectory
        let home = PathUtilities.canonicalPath(homeDirectory.standardizedFileURL.resolvingSymlinksInPath())
        homePath = home
        protectedPaths = Set([home] + Self.protectedHomeFolders.map { home + "/" + $0 })
        protectedSubtrees = [home + "/Library/Keychains"]
    }

    public func canDelete(_ url: URL) -> Bool {
        (try? validate(url)) != nil
    }

    /// Throws a `DeletionError` if `url` must not be deleted.
    public func validate(_ url: URL) throws {
        let path = Self.resolvedPath(of: url)
        if path == "/" {
            throw DeletionError.protectedLocation(path)
        }

        if PathUtilities.isPath(path, inside: homePath) {
            if protectedPaths.contains(path) || protectedSubtrees.contains(where: { PathUtilities.isPath(path, inside: $0) }) {
                throw DeletionError.protectedLocation(path)
            }
            return
        }

        for root in Self.allowedExternalRoots where PathUtilities.isPath(path, inside: root) {
            // The root itself, or a volume's mount point, is never deletable.
            let depth = path.split(separator: "/").count
            if path == root || (root == "/Volumes" && depth <= 2) {
                throw DeletionError.protectedLocation(path)
            }
            return
        }

        throw DeletionError.outsideAllowedLocations(path)
    }

    /// Resolves symbolic links in the parent folders but not in the item itself,
    /// so deleting a link deletes the link rather than its target.
    static func resolvedPath(of url: URL) -> String {
        let standardized = url.standardizedFileURL
        let path = PathUtilities.canonicalPath(standardized)
        if path == "/" { return path }
        let parent = standardized.deletingLastPathComponent().resolvingSymlinksInPath()
        return PathUtilities.canonicalPath(parent.appendingPathComponent(standardized.lastPathComponent))
    }
}
