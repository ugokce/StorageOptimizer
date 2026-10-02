import Foundation

/// The built-in list of cache locations.
public enum CacheCatalog {
    struct Definition {
        let id: String
        let title: String
        let detail: String
        let group: CacheGroup
        let safety: CacheSafety
        /// Paths relative to the home folder. A path component may contain `*` wildcards.
        let paths: [String]
        var action: CacheTarget.Action = .deleteContents
    }

    static let definitions: [Definition] = [
        // Xcode & Apple development
        Definition(
            id: "xcode-derived-data",
            title: "Xcode DerivedData",
            detail: "Build products, indexes and logs. Xcode recreates them on the next build.",
            group: .developer, safety: .safe,
            paths: ["Library/Developer/Xcode/DerivedData"]
        ),
        Definition(
            id: "xcode-previews",
            title: "Xcode Previews",
            detail: "Cached SwiftUI preview builds. Recreated the next time a preview runs.",
            group: .developer, safety: .safe,
            paths: ["Library/Developer/Xcode/UserData/Previews"]
        ),
        Definition(
            id: "xcode-device-support",
            title: "Device Support Files",
            detail: "Debug symbols copied from connected iPhones, iPads and watches. Xcode copies them again when a device reconnects.",
            group: .developer, safety: .safe,
            paths: ["Library/Developer/Xcode/*DeviceSupport"]
        ),
        Definition(
            id: "simulator-caches",
            title: "Simulator Caches",
            detail: "Runtime caches of the iOS Simulator. Recreated when a simulator boots.",
            group: .developer, safety: .safe,
            paths: ["Library/Developer/CoreSimulator/Caches"]
        ),
        Definition(
            id: "unavailable-simulators",
            title: "Unavailable Simulators",
            detail: "Simulators whose iOS runtime is no longer installed (runs xcrun simctl delete unavailable).",
            group: .developer, safety: .safe,
            paths: ["Library/Developer/CoreSimulator/Devices"],
            action: .command(executable: "/usr/bin/xcrun", arguments: ["simctl", "delete", "unavailable"])
        ),
        Definition(
            id: "swiftpm-cache",
            title: "Swift Package Manager Cache",
            detail: "Downloaded Swift packages. Fetched again when a project resolves its packages.",
            group: .developer, safety: .safe,
            paths: ["Library/Caches/org.swift.swiftpm"]
        ),
        Definition(
            id: "cocoapods-cache",
            title: "CocoaPods Cache",
            detail: "Downloaded pods. Fetched again on the next pod install.",
            group: .developer, safety: .safe,
            paths: ["Library/Caches/CocoaPods"]
        ),
        Definition(
            id: "carthage-cache",
            title: "Carthage Cache",
            detail: "Downloaded Carthage dependencies.",
            group: .developer, safety: .safe,
            paths: ["Library/Caches/org.carthage.CarthageKit"]
        ),
        Definition(
            id: "xcode-archives",
            title: "Xcode Archives",
            detail: "Archived app builds, including the dSYMs needed to symbolicate crash reports of released versions.",
            group: .developer, safety: .caution,
            paths: ["Library/Developer/Xcode/Archives"]
        ),

        // Package managers
        Definition(
            id: "homebrew-cache",
            title: "Homebrew",
            detail: "Downloaded bottles and source archives. Downloaded again when needed.",
            group: .packageManagers, safety: .safe,
            paths: ["Library/Caches/Homebrew"]
        ),
        Definition(
            id: "npm-cache",
            title: "npm",
            detail: "The npm package cache. Packages are downloaded again on the next install.",
            group: .packageManagers, safety: .safe,
            paths: [".npm/_cacache"]
        ),
        Definition(
            id: "yarn-cache",
            title: "Yarn",
            detail: "The Yarn package cache.",
            group: .packageManagers, safety: .safe,
            paths: ["Library/Caches/Yarn"]
        ),
        Definition(
            id: "pnpm-store",
            title: "pnpm Store",
            detail: "Projects installed with pnpm link into this store and need pnpm install afterwards.",
            group: .packageManagers, safety: .caution,
            paths: ["Library/pnpm/store"]
        ),
        Definition(
            id: "bun-cache",
            title: "Bun",
            detail: "The Bun install cache.",
            group: .packageManagers, safety: .safe,
            paths: [".bun/install/cache"]
        ),
        Definition(
            id: "pip-cache",
            title: "pip",
            detail: "Downloaded Python wheels and packages.",
            group: .packageManagers, safety: .safe,
            paths: ["Library/Caches/pip"]
        ),
        Definition(
            id: "gradle-cache",
            title: "Gradle",
            detail: "Downloaded dependencies and build caches. Downloaded again on the next build.",
            group: .packageManagers, safety: .safe,
            paths: [".gradle/caches"]
        ),
        Definition(
            id: "go-build-cache",
            title: "Go Build Cache",
            detail: "Compiled Go packages. Rebuilt on the next go build.",
            group: .packageManagers, safety: .safe,
            paths: ["Library/Caches/go-build"]
        ),
        Definition(
            id: "cargo-cache",
            title: "Cargo Registry Cache",
            detail: "Downloaded Rust crates.",
            group: .packageManagers, safety: .safe,
            paths: [".cargo/registry/cache"]
        ),

        // System & apps
        Definition(
            id: "user-caches",
            title: "App Caches",
            detail: "Temporary data apps keep in ~/Library/Caches. Apps recreate what they need; quit them first for best results.",
            group: .system, safety: .safe,
            paths: ["Library/Caches"]
        ),
        Definition(
            id: "user-logs",
            title: "Logs",
            detail: "Log files written by apps and diagnostic tools.",
            group: .system, safety: .safe,
            paths: ["Library/Logs"]
        ),
        Definition(
            id: "trash",
            title: "Trash",
            detail: "Items already in the Trash. Emptying it can't be undone.",
            group: .system, safety: .caution,
            paths: [".Trash"]
        ),
        Definition(
            id: "ios-backups",
            title: "iPhone & iPad Backups",
            detail: "Local device backups made with Finder. Delete them only if you have another backup, such as iCloud.",
            group: .system, safety: .caution,
            paths: ["Library/Application Support/MobileSync/Backup"]
        ),
    ]

    /// The catalog targets whose folders exist under `home`, with overlapping locations resolved.
    public static func availableTargets(home: URL = FileManager.default.homeDirectoryForCurrentUser) -> [CacheTarget] {
        let targets = definitions.compactMap { definition -> CacheTarget? in
            let locations = definition.paths
                .flatMap { expand($0, home: home) }
                .filter(isDirectory)
            guard !locations.isEmpty else { return nil }
            if case let .command(executable, _) = definition.action,
               !FileManager.default.isExecutableFile(atPath: executable) {
                return nil
            }
            return CacheTarget(
                id: definition.id,
                title: definition.title,
                detail: definition.detail,
                group: definition.group,
                safety: definition.safety,
                locations: locations,
                action: definition.action
            )
        }
        return resolveOverlaps(targets)
    }

    /// Prevents targets from measuring or deleting each other's data.
    ///
    /// When a location of one target lies inside a folder another target empties (Homebrew's
    /// cache inside `~/Library/Caches`, for example), the outer target excludes it. Project
    /// folders that already live inside a catalog location are dropped.
    public static func resolveOverlaps(_ targets: [CacheTarget]) -> [CacheTarget] {
        let catalogPaths = targets
            .filter { $0.group != .projects }
            .flatMap { $0.locations.map { PathUtilities.canonicalPath($0) } }

        var resolved = targets.filter { target in
            guard target.group == .projects else { return true }
            return !target.locations.contains { location in
                let path = PathUtilities.canonicalPath(location)
                return catalogPaths.contains { PathUtilities.isPath(path, inside: $0) }
            }
        }

        for index in resolved.indices {
            guard resolved[index].action == .deleteContents else {
                resolved[index].excludedPaths = []
                continue
            }
            let ownPaths = resolved[index].locations.map { PathUtilities.canonicalPath($0) }
            var excluded: [URL] = []
            for otherIndex in resolved.indices where otherIndex != index {
                for location in resolved[otherIndex].locations {
                    let path = PathUtilities.canonicalPath(location)
                    let isNested = ownPaths.contains { own in own != path && PathUtilities.isPath(path, inside: own) }
                    if isNested {
                        excluded.append(location)
                    }
                }
            }
            resolved[index].excludedPaths = excluded
        }
        return resolved
    }

    /// Expands a home-relative path whose components may contain `*` wildcards.
    static func expand(_ pattern: String, home: URL) -> [URL] {
        var results = [home]
        for component in pattern.split(separator: "/").map(String.init) {
            if component.contains("*") {
                results = results.flatMap { base -> [URL] in
                    let names = (try? FileManager.default.contentsOfDirectory(atPath: base.path)) ?? []
                    return names
                        .filter { wildcardMatches($0, pattern: component) }
                        .sorted()
                        .map { base.appendingPathComponent($0) }
                }
            } else {
                results = results.map { $0.appendingPathComponent(component) }
            }
        }
        return results
    }

    /// Matches `name` against a pattern where `*` stands for any run of characters.
    static func wildcardMatches(_ name: String, pattern: String) -> Bool {
        let parts = pattern.split(separator: "*", omittingEmptySubsequences: false).map(String.init)
        guard parts.count > 1, let first = parts.first, let last = parts.last else { return name == pattern }
        guard name.hasPrefix(first), name.count >= first.count + last.count else { return false }
        var remainder = name.dropFirst(first.count)
        for middle in parts.dropFirst().dropLast() where !middle.isEmpty {
            guard let range = remainder.range(of: middle) else { return false }
            remainder = remainder[range.upperBound...]
        }
        return remainder.hasSuffix(last)
    }

    private static func isDirectory(_ url: URL) -> Bool {
        var isDirectory: ObjCBool = false
        return FileManager.default.fileExists(atPath: url.path, isDirectory: &isDirectory) && isDirectory.boolValue
    }
}
