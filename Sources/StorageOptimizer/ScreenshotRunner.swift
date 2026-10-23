import AppKit
import StorageCore

/// `StorageOptimizer --screenshots <folder>`: scans a generated demo folder, saves a screenshot of
/// every page in light and dark mode, writes `screenshots.log` and quits with 0 or 1. The
/// Screenshots workflow uses it to keep the README's images current.
@MainActor
final class ScreenshotRunner {
    static let windowSize = NSSize(width: 1240, height: 820)

    private let outputDirectory: URL
    private let appState: AppState
    private var lines: [String] = []
    private var failures = 0

    init(outputDirectory: URL, appState: AppState) {
        self.outputDirectory = outputDirectory
        self.appState = appState
    }

    static func outputDirectoryFromCommandLine() -> URL? {
        let arguments = CommandLine.arguments
        guard let index = arguments.firstIndex(of: "--screenshots") else { return nil }
        let path = arguments.indices.contains(index + 1)
            ? arguments[index + 1]
            : FileManager.default.temporaryDirectory.appendingPathComponent("StorageOptimizerScreenshots").path
        return URL(fileURLWithPath: path, isDirectory: true).standardizedFileURL
    }

    func run() async {
        let demo = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Demo", isDirectory: true)
        var createdDemo = false
        do {
            try FileManager.default.createDirectory(at: outputDirectory, withIntermediateDirectories: true)
            try DemoFolder.create(at: demo)
            createdDemo = true
            try await capturePages(demo: demo)
        } catch {
            fail("Screenshot run failed: \(error)")
        }
        if createdDemo {
            try? FileManager.default.removeItem(at: demo)
        }
        finish()
    }

    private func capturePages(demo: URL) async throws {
        appState.prepareForScreenshots()
        appState.cacheModel.refresh()
        appState.scanTarget = .custom(demo)
        appState.startScan()

        guard await waitUntil(seconds: 180, { !appState.isScanning }),
              appState.phase == .finished, let result = appState.result
        else {
            fail("The scan didn't finish: \(appState.phase)")
            return
        }
        log("Scanned \(result.fileCount) files, \(ByteCountFormatter.string(fromByteCount: result.totalSize, countStyle: .file)), \(result.artifactDirectories.count) project folders.")

        let cacheModel = appState.cacheModel
        if !(await waitUntil(seconds: 240, { !cacheModel.isRefreshing && !cacheModel.isMeasuringAnything })) {
            log("Cache Cleaner was still measuring after 4 minutes.")
        }
        log("Cache Cleaner found \(cacheModel.rows.count) targets.")

        guard let window = NSApp.windows.first(where: { $0.isVisible && $0.contentView != nil && !($0 is NSPanel) }) else {
            fail("The app has no window.")
            return
        }
        let screen = window.screen ?? NSScreen.main
        let visible = screen?.visibleFrame ?? NSRect(origin: .zero, size: Self.windowSize)
        window.setFrame(
            NSRect(
                x: visible.minX + 20,
                y: max(visible.minY, visible.maxY - Self.windowSize.height - 20),
                width: Self.windowSize.width,
                height: Self.windowSize.height
            ),
            display: true
        )
        // An inactive window draws its sidebar and title bar dimmed. The Screenshots workflow
        // brings the app to the front, since it can't always activate itself when started from a shell.
        window.makeKeyAndOrderFront(nil)
        let active = await waitUntil(seconds: 20) {
            NSApp.activate()
            return NSApp.isActive
        }
        log(active ? "The app is active." : "The app didn't become active; windows look inactive.")
        log("Screen \(Int(screen?.frame.width ?? 0))x\(Int(screen?.frame.height ?? 0)) at \(screen?.backingScaleFactor ?? 1)x, window \(Int(window.frame.width))x\(Int(window.frame.height)).")

        let appearances: [(NSAppearance.Name, String)] = [(.aqua, "light"), (.darkAqua, "dark")]
        for (appearance, suffix) in appearances {
            NSApp.appearance = NSAppearance(named: appearance)
            for item in SidebarItem.allCases {
                appState.selection = item
                try await Task.sleep(nanoseconds: 1_500_000_000)
                await capture(window, name: "\(item.title.lowercased().replacingOccurrences(of: " ", with: "-"))-\(suffix)")
            }
        }
    }

    // MARK: - Capturing

    /// Prefers `screencapture`, which shows the window exactly as on screen (vibrancy included).
    /// Falls back to drawing the window's views, which needs no Screen Recording permission.
    private func capture(_ window: NSWindow, name: String) async {
        let url = outputDirectory.appendingPathComponent(name + ".png")
        try? FileManager.default.removeItem(at: url)
        if await captureWithScreencapture(window, to: url) {
            log("\(name): screencapture")
        } else if captureWithCacheDisplay(window, to: url) {
            log("\(name): cacheDisplay (screencapture gave no usable image)")
        } else {
            fail("\(name): couldn't capture the window")
        }
    }

    private func captureWithScreencapture(_ window: NSWindow, to url: URL) async -> Bool {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        process.arguments = ["-x", "-o", "-l\(window.windowNumber)", url.path]
        let watchdog = Task { [process] in
            try? await Task.sleep(nanoseconds: 15_000_000_000)
            if process.isRunning { process.terminate() }
        }
        defer { watchdog.cancel() }
        let status: Int32? = await withCheckedContinuation { continuation in
            process.terminationHandler = { finished in
                continuation.resume(returning: finished.terminationStatus)
            }
            do {
                try process.run()
            } catch {
                continuation.resume(returning: nil)
            }
        }
        guard status == 0, let data = try? Data(contentsOf: url), let image = NSBitmapImageRep(data: data),
              image.pixelsWide >= Int(window.frame.width * 0.9), image.pixelsHigh >= Int(window.frame.height * 0.9),
              Self.hasDetail(image)
        else {
            try? FileManager.default.removeItem(at: url)
            return false
        }
        return true
    }

    private func captureWithCacheDisplay(_ window: NSWindow, to url: URL) -> Bool {
        guard let view = window.contentView?.superview ?? window.contentView,
              let snapshot = view.bitmapImageRepForCachingDisplay(in: view.bounds)
        else { return false }
        view.cacheDisplay(in: view.bounds, to: snapshot)

        // Translucent areas (sidebar, toolbar) are drawn over the window background color.
        guard let image = NSBitmapImageRep(
            bitmapDataPlanes: nil, pixelsWide: snapshot.pixelsWide, pixelsHigh: snapshot.pixelsHigh,
            bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
            colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
        ), let context = NSGraphicsContext(bitmapImageRep: image)
        else { return false }
        image.size = snapshot.size
        let bounds = NSRect(origin: .zero, size: snapshot.size)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = context
        window.effectiveAppearance.performAsCurrentDrawingAppearance {
            NSColor.windowBackgroundColor.setFill()
            bounds.fill()
        }
        _ = snapshot.draw(in: bounds, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: false, hints: nil)
        NSGraphicsContext.restoreGraphicsState()

        guard let data = image.representation(using: .png, properties: [:]) else { return false }
        return (try? data.write(to: url)) != nil
    }

    /// Whether an image shows more than a few flat colors, so a blank or black capture is rejected.
    private static func hasDetail(_ image: NSBitmapImageRep) -> Bool {
        var colors = Set<Int>()
        for row in 0..<16 {
            for column in 0..<16 {
                let x = image.pixelsWide * (2 * column + 1) / 32
                let y = image.pixelsHigh * (2 * row + 1) / 32
                guard let color = image.colorAt(x: x, y: y)?.usingColorSpace(.sRGB) else { continue }
                colors.insert(Int(color.redComponent * 31) << 10 | Int(color.greenComponent * 31) << 5 | Int(color.blueComponent * 31))
            }
        }
        return colors.count >= 8
    }

    // MARK: - Logging

    private func waitUntil(seconds: TimeInterval, _ condition: () -> Bool) async -> Bool {
        let deadline = Date(timeIntervalSinceNow: seconds)
        while !condition() {
            if Date() > deadline { return false }
            try? await Task.sleep(nanoseconds: 200_000_000)
        }
        return true
    }

    private func log(_ message: String) {
        lines.append(message)
        print(message)
    }

    private func fail(_ message: String) {
        failures += 1
        log("FAIL: " + message)
    }

    private func finish() -> Never {
        log(failures == 0 ? "PASSED" : "FAILED with \(failures) problem(s)")
        let text = lines.joined(separator: "\n") + "\n"
        try? text.write(to: outputDirectory.appendingPathComponent("screenshots.log"), atomically: true, encoding: .utf8)
        exit(failures == 0 ? 0 : 1)
    }
}

/// A folder of sparse files that looks like a typical, rather full home folder, so every page and
/// file type has something to show. The files take almost no real disk space.
enum DemoFolder {
    private struct Entry {
        let path: String
        let megabytes: Double
        var daysAgo: Int? = nil
    }

    /// Refuses to touch a folder that already exists, since it is deleted again afterwards.
    static func create(at root: URL) throws {
        let fileManager = FileManager.default
        guard !fileManager.fileExists(atPath: root.path) else {
            throw CocoaError(.fileWriteFileExists, userInfo: [NSFilePathErrorKey: root.path])
        }
        do {
            try populate(root)
        } catch {
            try? fileManager.removeItem(at: root)
            throw error
        }
    }

    private static func populate(_ root: URL) throws {
        let fileManager = FileManager.default
        var packageDates: [URL: Date] = [:]
        for (index, entry) in entries().enumerated() {
            var url = root.appendingPathComponent(entry.path)
            try fileManager.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            guard fileManager.createFile(atPath: url.path, contents: nil) else {
                throw CocoaError(.fileWriteUnknown, userInfo: [NSFilePathErrorKey: url.path])
            }
            let handle = try FileHandle(forWritingTo: url)
            try handle.truncate(atOffset: UInt64(entry.megabytes * 1_000_000))
            try handle.close()

            // Spread the dates over two years so "last opened" and the 6-month filter have variety.
            let date = Date(timeIntervalSinceNow: -Double(entry.daysAgo ?? (index * 37) % 720) * 86_400)
            var values = URLResourceValues()
            values.contentModificationDate = date
            values.contentAccessDate = date
            try url.setResourceValues(values)
            if let package = packageRoot(of: entry.path, in: root), packageDates[package] == nil {
                packageDates[package] = date
            }
        }

        // Adding files to a package updated its dates, and Large Files lists packages by their own.
        for (package, date) in packageDates {
            var url = package
            var values = URLResourceValues()
            values.contentModificationDate = date
            values.contentAccessDate = date
            try url.setResourceValues(values)
        }
    }

    private static func packageRoot(of path: String, in root: URL) -> URL? {
        let components = path.split(separator: "/")
        guard let index = components.firstIndex(where: { $0.hasSuffix(".app") || $0.hasSuffix(".photoslibrary") }) else {
            return nil
        }
        return root.appendingPathComponent(components[...index].joined(separator: "/"), isDirectory: true)
    }

    private static func entries() -> [Entry] {
        var entries: [Entry] = [
            Entry(path: "Movies/Lisbon Trip 2025.mov", megabytes: 8_600, daysAgo: 40),
            Entry(path: "Movies/Screen Recording 2025-03-14.mov", megabytes: 2_400, daysAgo: 220),
            Entry(path: "Movies/Drone Footage/DJI_0042.mp4", megabytes: 1_900, daysAgo: 410),
            Entry(path: "Movies/Drone Footage/DJI_0043.mp4", megabytes: 1_600, daysAgo: 410),
            Entry(path: "Pictures/Design/Festival Poster.psd", megabytes: 780, daysAgo: 300),
            Entry(path: "Downloads/Xcode_16.4.xip", megabytes: 4_400, daysAgo: 120),
            Entry(path: "Downloads/ubuntu-24.04.2-desktop-arm64.iso", megabytes: 3_100, daysAgo: 260),
            Entry(path: "Downloads/Docker.dmg", megabytes: 640, daysAgo: 90),
            Entry(path: "Downloads/Blender-4.4.dmg", megabytes: 380, daysAgo: 200),
            Entry(path: "Downloads/Invoice-2025-08.pdf", megabytes: 0.4, daysAgo: 70),
            Entry(path: "Documents/Backups/Website backup 2023.zip", megabytes: 3_400, daysAgo: 600),
            Entry(path: "Documents/Quarterly Review.key", megabytes: 640, daysAgo: 30),
            Entry(path: "Documents/Thesis.pdf", megabytes: 48, daysAgo: 500),
            Entry(path: "Documents/Taxes 2024.pdf", megabytes: 12, daysAgo: 150),
            Entry(path: "Virtual Machines/Ubuntu Server.vmdk", megabytes: 9_800, daysAgo: 380),
            Entry(path: "Applications/Unity.app/Contents/MacOS/Unity", megabytes: 420, daysAgo: 60),
            Entry(path: "Applications/Unity.app/Contents/Frameworks/UnityEngine.framework/UnityEngine", megabytes: 1_900, daysAgo: 60),
            Entry(path: "Applications/Unity.app/Contents/Resources/data.unity3d", megabytes: 3_200, daysAgo: 60),
            Entry(path: "Applications/Sketch.app/Contents/MacOS/Sketch", megabytes: 280, daysAgo: 250),
            Entry(path: "Developer/website/package.json", megabytes: 0.002, daysAgo: 5),
            Entry(path: "Developer/api/Cargo.toml", megabytes: 0.001, daysAgo: 12),
            Entry(path: "Developer/api/src/main.rs", megabytes: 0.02, daysAgo: 12),
            Entry(path: "Developer/api/target/debug/api", megabytes: 180, daysAgo: 12),
            Entry(path: "Developer/ios-app/Package.swift", megabytes: 0.001, daysAgo: 25),
            Entry(path: "Developer/ml-experiments/.venv/pyvenv.cfg", megabytes: 0.001, daysAgo: 300),
            Entry(path: "Developer/ml-experiments/.venv/lib/python3.12/site-packages/torch/lib/libtorch_cpu.dylib", megabytes: 1_400, daysAgo: 300),
            Entry(path: "Developer/ml-experiments/train.py", megabytes: 0.01, daysAgo: 300),
        ]
        for index in 1...320 {
            entries.append(Entry(
                path: "Pictures/Photos Library.photoslibrary/originals/\(String(index % 16, radix: 16, uppercase: true))/IMG_\(4000 + index).heic",
                megabytes: 34
            ))
        }
        for index in 1...24 {
            entries.append(Entry(path: "Pictures/Wallpapers/Wallpaper \(index).jpg", megabytes: 6))
        }
        for album in 1...4 {
            for track in 1...12 {
                entries.append(Entry(path: "Music/Lossless/Album \(album)/Track \(track).flac", megabytes: 32))
            }
        }
        for episode in 1...30 {
            entries.append(Entry(path: "Music/Podcasts/Episode \(episode).mp3", megabytes: 60))
        }
        for note in 1...150 {
            entries.append(Entry(path: "Documents/Notes/Note \(note).md", megabytes: 0.008))
        }
        for year in 2021...2025 {
            entries.append(Entry(path: "Documents/Spreadsheets/Budget \(year).xlsx", megabytes: 2))
        }
        for page in 1...20 {
            entries.append(Entry(path: "Developer/website/src/app/page\(page).tsx", megabytes: 0.01, daysAgo: 5))
        }
        for package in 1...40 {
            entries.append(Entry(path: "Developer/website/node_modules/package-\(package)/index.js", megabytes: 6, daysAgo: 5))
            entries.append(Entry(path: "Developer/website/node_modules/package-\(package)/dist/bundle.js", megabytes: 18, daysAgo: 5))
        }
        for pack in 1...12 {
            entries.append(Entry(path: "Developer/website/.next/cache/webpack/client-production/\(pack).pack", megabytes: 45, daysAgo: 5))
        }
        for crate in 1...60 {
            entries.append(Entry(path: "Developer/api/target/debug/deps/libcrate_\(crate).rlib", megabytes: 42, daysAgo: 12))
        }
        for object in 1...50 {
            entries.append(Entry(path: "Developer/ios-app/.build/arm64-apple-macosx/debug/Module\(object).o", megabytes: 30, daysAgo: 25))
        }
        return entries
    }
}
