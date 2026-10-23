import AppKit
import Observation
import StorageCore
import SwiftUI

enum SidebarItem: String, CaseIterable, Identifiable, Hashable {
    case overview
    case largeFiles
    case folders
    case fileTypes
    case cacheCleaner

    var id: String { rawValue }

    var title: String {
        switch self {
        case .overview: "Overview"
        case .largeFiles: "Large Files"
        case .folders: "Folders"
        case .fileTypes: "File Types"
        case .cacheCleaner: "Cache Cleaner"
        }
    }

    var systemImage: String {
        switch self {
        case .overview: "internaldrive"
        case .largeFiles: "doc.text.magnifyingglass"
        case .folders: "folder"
        case .fileTypes: "square.grid.2x2"
        case .cacheCleaner: "sparkles"
        }
    }
}

enum ScanTarget: Hashable, Sendable {
    case home
    case entireDisk
    case custom(URL)

    var url: URL {
        switch self {
        case .home: FileManager.default.homeDirectoryForCurrentUser
        case .entireDisk: URL(fileURLWithPath: "/")
        case .custom(let url): url
        }
    }

    var title: String {
        switch self {
        case .home: "Home Folder"
        case .entireDisk: "Entire Disk"
        case .custom(let url): url.lastPathComponent
        }
    }

    var systemImage: String {
        switch self {
        case .home: "house"
        case .entireDisk: "internaldrive"
        case .custom: "folder"
        }
    }
}

enum ScanPhase: Equatable {
    case idle
    case scanning
    case finished
    case cancelled
    case failed(String)
}

@MainActor
@Observable
final class AppState {
    var selection: SidebarItem? = .overview
    var scanTarget: ScanTarget = .home
    /// The folder browser's navigation stack, below the scan root.
    var folderPath: [FileNode] = []
    var isAlertPresented = false

    private(set) var phase: ScanPhase = .idle
    private(set) var progress = ScanProgress.Snapshot()
    private(set) var result: ScanResult?
    private(set) var volume: VolumeInfo?
    private(set) var hasFullDiskAccess = true
    private(set) var alertTitle = ""
    private(set) var alertMessage = ""

    let cacheModel: CacheCleanerModel

    @ObservationIgnored private var scanTask: Task<Void, Never>?
    /// Set by screenshot mode (see `ScreenshotRunner`).
    @ObservationIgnored private var isScreenshotMode = false

    init() {
        cacheModel = CacheCleanerModel()
        cacheModel.onItemsRemoved = { [weak self] urls in
            self?.applyRemovals(urls)
        }
        refreshEnvironment()
    }

    var isScanning: Bool { phase == .scanning }

    /// Re-reads disk capacity and Full Disk Access status.
    func refreshEnvironment() {
        volume = VolumeInfo.current()
        hasFullDiskAccess = isScreenshotMode || FullDiskAccess.isGranted()
    }

    /// Hides the Full Disk Access banner and counts files at their full length, so the sparse
    /// files of the screenshot demo folder and sample caches show realistic sizes.
    func prepareForScreenshots() {
        isScreenshotMode = true
        cacheModel.sizeMode = .logical
        refreshEnvironment()
    }

    // MARK: - Scanning

    func startScan() {
        guard !isScanning else { return }
        let target = scanTarget
        let options = ScanOptions(
            sizeMode: isScreenshotMode ? .logical : .allocated,
            skipPaths: target == .entireDisk ? ScanOptions.entireDiskSkipPaths : []
        )
        let scanner = DiskScanner(options: options)
        let tracker = ScanProgress()

        phase = .scanning
        progress = ScanProgress.Snapshot()

        scanTask = Task { [weak self] in
            let worker = Task.detached(priority: .userInitiated) {
                try await scanner.scan(root: target.url, progress: tracker)
            }
            let poller = Task {
                while !Task.isCancelled {
                    try? await Task.sleep(nanoseconds: 200_000_000)
                    self?.progress = tracker.snapshot()
                }
            }
            defer { poller.cancel() }

            do {
                let result = try await withTaskCancellationHandler {
                    try await worker.value
                } onCancel: {
                    worker.cancel()
                }
                self?.finishScan(with: result)
            } catch is CancellationError {
                self?.phase = .cancelled
            } catch {
                self?.phase = .failed(error.localizedDescription)
                self?.presentAlert(title: "Scan failed", message: error.localizedDescription)
            }
        }
    }

    func cancelScan() {
        scanTask?.cancel()
    }

    func chooseFolderAndScan() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.prompt = "Scan"
        panel.message = "Choose a folder to analyze."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        scanTarget = .custom(url)
        startScan()
    }

    private func finishScan(with result: ScanResult) {
        self.result = result
        folderPath = []
        phase = .finished
        progress = ScanProgress.Snapshot(
            filesScanned: result.fileCount,
            bytesScanned: result.totalSize,
            currentPath: ""
        )
        cacheModel.setProjectArtifacts(result.artifactDirectories)
        refreshEnvironment()
    }

    // MARK: - File actions

    /// Moves items to the Trash after checking them against the deletion guard.
    func moveToTrash(_ urls: [URL]) {
        let deletionGuard = DeletionGuard()
        var failures: [String] = []
        for url in urls {
            do {
                try deletionGuard.validate(url)
                try FileManager.default.trashItem(at: url, resultingItemURL: nil)
                result?.applyRemoval(of: url)
            } catch {
                failures.append("\(url.lastPathComponent): \(error.localizedDescription)")
            }
        }
        refreshEnvironment()
        if !failures.isEmpty {
            presentAlert(
                title: "Some items couldn't be moved to the Trash",
                message: failures.prefix(8).joined(separator: "\n")
            )
        }
    }

    /// Opens a folder of the scan in the folder browser.
    func showInFolders(_ node: FileNode) {
        var path: [FileNode] = []
        var current: FileNode? = node
        while let item = current, item !== result?.root {
            path.insert(item, at: 0)
            current = item.parent
        }
        folderPath = path
        selection = .folders
    }

    private func applyRemovals(_ urls: [URL]) {
        for url in urls {
            result?.applyRemoval(of: url)
        }
        refreshEnvironment()
    }

    func presentAlert(title: String, message: String) {
        alertTitle = title
        alertMessage = message
        isAlertPresented = true
    }
}
