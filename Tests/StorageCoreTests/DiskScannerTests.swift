import XCTest
@testable import StorageCore

final class DiskScannerTests: XCTestCase {
    private func makeScanner(topFileCount: Int = 2_000, skipPaths: Set<String> = []) -> DiskScanner {
        DiskScanner(options: ScanOptions(
            sizeMode: .logical,
            detailThreshold: 1_000_000,
            topFileCount: topFileCount,
            skipPaths: skipPaths
        ))
    }

    private func makeBasicFixture() throws -> TemporaryDirectory {
        let directory = try makeTemporaryDirectory()
        try directory.makeFile("big.mov", size: 3_000_000)
        try directory.makeFile("docs/report.pdf", size: 2_000_000)
        try directory.makeFile("docs/notes.txt", size: 100)
        try directory.makeFile("docs/todo.txt", size: 200)
        return directory
    }

    func testAggregatesSizesAndSmallFiles() async throws {
        let directory = try makeBasicFixture()
        let result = try await makeScanner().scan(root: directory.url)

        XCTAssertEqual(result.totalSize, 5_000_300)
        XCTAssertEqual(result.fileCount, 4)
        XCTAssertEqual(result.root.children.map(\.name), ["big.mov", "docs"])

        let docs = try XCTUnwrap(result.root.children.first { $0.name == "docs" })
        XCTAssertEqual(docs.size, 2_000_300)
        XCTAssertEqual(docs.fileCount, 3)
        XCTAssertEqual(docs.children.map(\.name), ["report.pdf"])
        XCTAssertEqual(docs.smallFilesCount, 2)
        XCTAssertEqual(docs.smallFilesSize, 300)
        XCTAssertTrue(docs.children.first?.parent === docs)
        XCTAssertTrue(docs.parent === result.root)
    }

    func testLargestFilesAndCategoryStatistics() async throws {
        let directory = try makeBasicFixture()
        let result = try await makeScanner().scan(root: directory.url)

        XCTAssertEqual(result.largestFiles.map(\.name), ["big.mov", "report.pdf", "todo.txt", "notes.txt"])
        XCTAssertEqual(result.categoryStats[.video], UsageStats(size: 3_000_000, count: 1))
        XCTAssertEqual(result.categoryStats[.document], UsageStats(size: 2_000_300, count: 3))
        XCTAssertEqual(result.categoryFiles[.document]?.first?.name, "report.pdf")
        XCTAssertEqual(result.categorySummaries.map(\.category), [.video, .document])
        XCTAssertEqual(result.extensionSummaries(for: .document).map(\.fileExtension), ["pdf", "txt"])
    }

    func testLargestFilesAreBounded() async throws {
        let directory = try makeTemporaryDirectory()
        for index in 0..<20 {
            try directory.makeFile("file\(index).bin", size: 1_000 + index)
        }
        let result = try await makeScanner(topFileCount: 5).scan(root: directory.url)

        XCTAssertEqual(result.largestFiles.count, 5)
        XCTAssertEqual(result.largestFiles.map(\.size), [1_019, 1_018, 1_017, 1_016, 1_015])
        XCTAssertEqual(result.fileCount, 20)
    }

    func testSymbolicLinksAreNotFollowed() async throws {
        let directory = try makeTemporaryDirectory()
        let movie = try directory.makeFile("data/movie.mov", size: 2_000_000)
        try FileManager.default.createSymbolicLink(
            at: directory.url.appendingPathComponent("link.mov"),
            withDestinationURL: movie
        )
        try FileManager.default.createSymbolicLink(
            at: directory.url.appendingPathComponent("linked-folder"),
            withDestinationURL: movie.deletingLastPathComponent()
        )
        let result = try await makeScanner().scan(root: directory.url)

        XCTAssertEqual(result.totalSize, 2_000_000)
        XCTAssertEqual(result.fileCount, 1)
    }

    func testSkipPathsAreIgnored() async throws {
        let directory = try makeTemporaryDirectory()
        try directory.makeFile("keep/a.bin", size: 1_000)
        try directory.makeFile("skip/b.bin", size: 5_000)
        let skipped = directory.url.appendingPathComponent("skip").path
        let result = try await makeScanner(skipPaths: [skipped]).scan(root: directory.url)

        XCTAssertEqual(result.totalSize, 1_000)
        XCTAssertEqual(result.root.children.map(\.name), ["keep"])
    }

    func testPackagesAreReportedAsSingleItems() async throws {
        let directory = try makeTemporaryDirectory()
        try directory.makeFile("Tool.app/Contents/MacOS/Tool", size: 1_500_000)
        try directory.makeFile("Tool.app/Contents/Resources/icon.png", size: 500)
        let result = try await makeScanner().scan(root: directory.url)

        XCTAssertEqual(result.largestFiles.map(\.name), ["Tool.app"])
        XCTAssertEqual(result.largestFiles.first?.isPackage, true)
        XCTAssertEqual(result.categoryStats[.application], UsageStats(size: 1_500_500, count: 1))
        XCTAssertNil(result.categoryStats[.image])

        let app = try XCTUnwrap(result.root.children.first)
        XCTAssertTrue(app.isPackage)
        XCTAssertEqual(app.size, 1_500_500)
    }

    func testDetectsOutermostProjectBuildFolders() async throws {
        let directory = try makeTemporaryDirectory()
        try directory.makeFile("web/package.json", size: 10)
        try directory.makeFile("web/node_modules/left-pad/index.js", size: 100)
        try directory.makeFile("web/node_modules/left-pad/package.json", size: 10)
        try directory.makeFile("web/node_modules/left-pad/node_modules/dep/index.js", size: 50)
        try directory.makeFile("loose/node_modules/orphan.js", size: 10)
        try directory.makeFile("rust/Cargo.toml", size: 10)
        try directory.makeFile("rust/target/debug/app", size: 300)
        let result = try await makeScanner().scan(root: directory.url)

        XCTAssertEqual(result.artifactDirectories.count, 2)
        let nodeModules = try XCTUnwrap(result.artifactDirectories.first { $0.kind == .nodeModules })
        XCTAssertEqual(nodeModules.projectName, "web")
        XCTAssertEqual(nodeModules.size, 160)
        let cargoTarget = try XCTUnwrap(result.artifactDirectories.first { $0.kind == .cargoTarget })
        XCTAssertEqual(cargoTarget.projectName, "rust")
        XCTAssertEqual(cargoTarget.size, 300)
    }

    func testCancelledScanThrows() async throws {
        let directory = try makeTemporaryDirectory()
        try directory.makeFile("a.bin", size: 10)
        let scanner = makeScanner()
        let rootURL = directory.url

        let task = Task { () async throws -> ScanResult in
            withUnsafeCurrentTask { $0?.cancel() }
            return try await scanner.scan(root: rootURL)
        }
        do {
            _ = try await task.value
            XCTFail("Expected the scan to be cancelled")
        } catch is CancellationError {
            // Expected.
        }
    }

    func testScanningAFileThrows() async throws {
        let directory = try makeTemporaryDirectory()
        let file = try directory.makeFile("a.bin", size: 10)
        do {
            _ = try await makeScanner().scan(root: file)
            XCTFail("Expected an error")
        } catch is ScanError {
            // Expected.
        }
    }

    func testApplyRemovalUpdatesTreeAndStatistics() async throws {
        let directory = try makeBasicFixture()
        var result = try await makeScanner().scan(root: directory.url)

        result.applyRemoval(of: directory.url.appendingPathComponent("big.mov"))
        XCTAssertEqual(result.totalSize, 2_000_300)
        XCTAssertFalse(result.largestFiles.contains { $0.name == "big.mov" })
        XCTAssertEqual(result.categoryStats[.video]?.size, 0)

        // A small file has no node of its own; its folder's totals must still shrink.
        result.applyRemoval(of: directory.url.appendingPathComponent("docs/notes.txt"))
        let docs = try XCTUnwrap(result.node(at: directory.url.appendingPathComponent("docs")))
        XCTAssertEqual(docs.smallFilesCount, 1)
        XCTAssertEqual(docs.size, 2_000_200)
        XCTAssertEqual(result.totalSize, 2_000_200)
        XCTAssertEqual(result.fileCount, 2)
        XCTAssertEqual(result.categoryStats[.document], UsageStats(size: 2_000_200, count: 2))

        // Removing a folder removes everything listed inside it.
        result.applyRemoval(of: directory.url.appendingPathComponent("docs"))
        XCTAssertEqual(result.totalSize, 0)
        XCTAssertTrue(result.largestFiles.isEmpty)
        XCTAssertTrue(result.root.children.isEmpty)
    }
}
