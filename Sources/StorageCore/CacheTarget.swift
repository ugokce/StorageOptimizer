import Foundation

public enum CacheGroup: String, CaseIterable, Identifiable, Sendable {
    case developer
    case packageManagers
    case system
    case projects

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .developer: "Xcode & Apple Development"
        case .packageManagers: "Package Managers"
        case .system: "System & Apps"
        case .projects: "Project Build Folders"
        }
    }
}

public enum CacheSafety: String, Sendable {
    /// Recreated automatically when needed; selected by default.
    case safe
    /// Can be recreated but may hold something worth keeping; never selected by default.
    case caution
}

/// Something the cache cleaner can measure and clean.
public struct CacheTarget: Identifiable, Hashable, Sendable {
    public enum Action: Hashable, Sendable {
        /// Delete everything inside each location but keep the folders themselves.
        case deleteContents
        /// Delete each location itself (used for project build folders).
        case deleteItem
        /// Run a command line tool. Locations are only used to report the space freed.
        case command(executable: String, arguments: [String])
    }

    public let id: String
    public let title: String
    public let detail: String
    public let group: CacheGroup
    public let safety: CacheSafety
    public let locations: [URL]
    public let action: Action
    /// Locations that belong to other targets and must be left alone by this one.
    public internal(set) var excludedPaths: [URL]

    public init(
        id: String,
        title: String,
        detail: String,
        group: CacheGroup,
        safety: CacheSafety,
        locations: [URL],
        action: Action = .deleteContents,
        excludedPaths: [URL] = []
    ) {
        self.id = id
        self.title = title
        self.detail = detail
        self.group = group
        self.safety = safety
        self.locations = locations
        self.action = action
        self.excludedPaths = excludedPaths
    }

    /// A target for a rebuildable project folder found by a scan.
    public init(artifact: ArtifactDirectory) {
        self.init(
            id: Self.artifactID(for: artifact.url),
            title: "\(artifact.projectName) · \(artifact.kind.displayName)",
            detail: artifact.kind.restoreHint,
            group: .projects,
            safety: .safe,
            locations: [artifact.url],
            action: .deleteItem
        )
    }

    public static func artifactID(for url: URL) -> String {
        "artifact:" + PathUtilities.canonicalPath(url)
    }

    /// Whether the cleaner can tell how much space this target uses before cleaning.
    public var reportsSize: Bool {
        if case .command = action { return false }
        return true
    }

    /// Whether the target is selected by default.
    public var isRecommended: Bool {
        safety == .safe && group != .projects
    }
}
