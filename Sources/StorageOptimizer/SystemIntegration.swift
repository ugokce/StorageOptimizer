import AppKit
import Foundation

/// Finder and pasteboard helpers.
enum FinderActions {
    static func reveal(_ urls: [URL]) {
        guard !urls.isEmpty else { return }
        NSWorkspace.shared.activateFileViewerSelecting(urls)
    }

    static func open(_ url: URL) {
        NSWorkspace.shared.open(url)
    }

    static func copyPaths(_ urls: [URL]) {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setString(urls.map(\.path).joined(separator: "\n"), forType: .string)
    }

    static func icon(for url: URL) -> NSImage {
        NSWorkspace.shared.icon(forFile: url.path)
    }
}

/// Detects whether the app may read privacy-protected folders.
enum FullDiskAccess {
    /// Folders macOS only lets apps read with Full Disk Access.
    private static let protectedProbes = ["Library/Safari", "Library/Mail", "Library/Messages"]

    static func isGranted() -> Bool {
        let home = FileManager.default.homeDirectoryForCurrentUser
        for probe in protectedProbes {
            let url = home.appendingPathComponent(probe)
            guard FileManager.default.fileExists(atPath: url.path) else { continue }
            return (try? FileManager.default.contentsOfDirectory(atPath: url.path)) != nil
        }
        return true
    }

    static func openSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles") {
            NSWorkspace.shared.open(url)
        }
    }
}

/// Whether Xcode is currently running (cleaning its caches is best done with Xcode closed).
enum RunningApps {
    static var isXcodeRunning: Bool {
        !NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.dt.Xcode").isEmpty
    }
}
