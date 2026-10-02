import Foundation

/// The outcome of a cleaning run.
public struct CleanReport: Identifiable, Sendable {
    public struct Failure: Identifiable, Hashable, Sendable {
        public let id = UUID()
        public let path: String
        public let reason: String
    }

    public let id = UUID()
    public internal(set) var freedBytes: Int64 = 0
    public internal(set) var cleanedTargetIDs: [String] = []
    /// Items deleted as a whole (project build folders).
    public internal(set) var removedItems: [URL] = []
    public internal(set) var failures: [Failure] = []

    public init() {}
}

/// Measures and permanently deletes cache data.
public struct CacheCleaner: Sendable {
    public let deletionGuard: DeletionGuard
    public let sizeMode: SizeMode

    public init(deletionGuard: DeletionGuard = DeletionGuard(), sizeMode: SizeMode = .allocated) {
        self.deletionGuard = deletionGuard
        self.sizeMode = sizeMode
    }

    /// Bytes currently used by `target`, excluding locations owned by other targets.
    public func measure(_ target: CacheTarget) -> Int64 {
        let excluded = Set(target.excludedPaths.map { PathUtilities.canonicalPath($0) })
        return target.locations.reduce(Int64(0)) { total, location in
            total + DirectorySizer.size(of: location, mode: sizeMode, excluding: excluded)
        }
    }

    /// Cleans each target. Every deletion is checked by the deletion guard first.
    public func clean(_ targets: [CacheTarget]) -> CleanReport {
        var report = CleanReport()
        for target in targets {
            let before = measure(target)
            switch target.action {
            case .deleteContents:
                let excluded = Set(target.excludedPaths.map { PathUtilities.canonicalPath($0) })
                for location in target.locations {
                    removeContents(of: location, excluding: excluded, report: &report)
                }
            case .deleteItem:
                for location in target.locations {
                    if remove(location, report: &report) {
                        report.removedItems.append(location)
                    }
                }
            case let .command(executable, arguments):
                run(executable: executable, arguments: arguments, report: &report)
            }
            let after = measure(target)
            report.freedBytes += max(0, before - after)
            report.cleanedTargetIDs.append(target.id)
        }
        return report
    }

    private func removeContents(of directory: URL, excluding excluded: Set<String>, report: inout CleanReport) {
        let children: [URL]
        do {
            children = try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil, options: [])
        } catch {
            report.failures.append(.init(path: directory.path, reason: error.localizedDescription))
            return
        }

        for child in children {
            let childPath = PathUtilities.canonicalPath(child)
            if excluded.contains(childPath) { continue }
            if excluded.contains(where: { PathUtilities.isPath($0, inside: childPath) }) {
                // Something below this child belongs to another target: only clean around it.
                removeContents(of: child, excluding: excluded, report: &report)
                continue
            }
            remove(child, report: &report)
        }
    }

    @discardableResult
    private func remove(_ url: URL, report: inout CleanReport) -> Bool {
        do {
            try deletionGuard.validate(url)
            try FileManager.default.removeItem(at: url)
            return true
        } catch {
            report.failures.append(.init(path: url.path, reason: error.localizedDescription))
            return false
        }
    }

    private func run(executable: String, arguments: [String], report: inout CleanReport) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice
        let errorPipe = Pipe()
        process.standardError = errorPipe
        let commandLine = ([executable] + arguments).joined(separator: " ")
        do {
            try process.run()
            let errorData = errorPipe.fileHandleForReading.readDataToEndOfFile()
            process.waitUntilExit()
            if process.terminationStatus != 0 {
                let message = String(decoding: errorData, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
                report.failures.append(.init(
                    path: commandLine,
                    reason: message.isEmpty ? "Exited with status \(process.terminationStatus)." : message
                ))
            }
        } catch {
            report.failures.append(.init(path: commandLine, reason: error.localizedDescription))
        }
    }
}
