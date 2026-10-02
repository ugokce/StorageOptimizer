import XCTest
@testable import StorageCore

final class CacheCleanerTests: XCTestCase {
    private func makeHomeFixture() throws -> (TemporaryDirectory, URL) {
        let directory = try makeTemporaryDirectory()
        let home = try directory.makeDirectory("home")
        try directory.makeFile("home/Library/Caches/com.example.app/blob", size: 1_000)
        try directory.makeFile("home/Library/Caches/Homebrew/bottle.tar.gz", size: 500)
        try directory.makeDirectory("home/Library/Developer/Xcode/iOS DeviceSupport/17.0")
        try directory.makeDirectory("home/Library/Developer/Xcode/watchOS DeviceSupport/10.0")
        return (directory, home)
    }

    func testCatalogFindsExistingLocationsAndResolvesOverlaps() throws {
        let (_, home) = try makeHomeFixture()
        let targets = CacheCatalog.availableTargets(home: home)

        XCTAssertEqual(Set(targets.map(\.id)), ["user-caches", "homebrew-cache", "xcode-device-support"])

        let deviceSupport = try XCTUnwrap(targets.first { $0.id == "xcode-device-support" })
        XCTAssertEqual(deviceSupport.locations.map(\.lastPathComponent), ["iOS DeviceSupport", "watchOS DeviceSupport"])

        let caches = try XCTUnwrap(targets.first { $0.id == "user-caches" })
        XCTAssertEqual(caches.excludedPaths.map(\.lastPathComponent), ["Homebrew"])

        let homebrew = try XCTUnwrap(targets.first { $0.id == "homebrew-cache" })
        let cleaner = CacheCleaner(deletionGuard: DeletionGuard(homeDirectory: home), sizeMode: .logical)
        XCTAssertEqual(cleaner.measure(caches), 1_000)
        XCTAssertEqual(cleaner.measure(homebrew), 500)
    }

    func testCleaningKeepsTheFolderAndOtherTargetsData() throws {
        let (directory, home) = try makeHomeFixture()
        let caches = try XCTUnwrap(CacheCatalog.availableTargets(home: home).first { $0.id == "user-caches" })
        let cleaner = CacheCleaner(deletionGuard: DeletionGuard(homeDirectory: home), sizeMode: .logical)

        let report = cleaner.clean([caches])

        XCTAssertTrue(directory.exists("home/Library/Caches"))
        XCTAssertFalse(directory.exists("home/Library/Caches/com.example.app"))
        XCTAssertTrue(directory.exists("home/Library/Caches/Homebrew/bottle.tar.gz"))
        XCTAssertEqual(report.freedBytes, 1_000)
        XCTAssertEqual(report.cleanedTargetIDs, ["user-caches"])
        XCTAssertTrue(report.failures.isEmpty)
    }

    func testNestedExclusionsArePreserved() throws {
        let directory = try makeTemporaryDirectory()
        let home = try directory.makeDirectory("home")
        let cache = try directory.makeDirectory("home/cache")
        try directory.makeFile("home/cache/top.dat", size: 10)
        try directory.makeFile("home/cache/vendor/other/file.dat", size: 20)
        let keep = try directory.makeDirectory("home/cache/vendor/keep")
        try directory.makeFile("home/cache/vendor/keep/file.dat", size: 30)

        let outer = CacheTarget(id: "outer", title: "Outer", detail: "", group: .system, safety: .safe, locations: [cache])
        let inner = CacheTarget(id: "inner", title: "Inner", detail: "", group: .system, safety: .safe, locations: [keep])
        let resolved = CacheCatalog.resolveOverlaps([outer, inner])
        let cleaner = CacheCleaner(deletionGuard: DeletionGuard(homeDirectory: home), sizeMode: .logical)

        XCTAssertEqual(cleaner.measure(resolved[0]), 30)
        let report = cleaner.clean([resolved[0]])

        XCTAssertFalse(directory.exists("home/cache/top.dat"))
        XCTAssertFalse(directory.exists("home/cache/vendor/other"))
        XCTAssertTrue(directory.exists("home/cache/vendor/keep/file.dat"))
        XCTAssertEqual(report.freedBytes, 30)
    }

    func testDeleteItemRemovesProjectFolder() throws {
        let directory = try makeTemporaryDirectory()
        let home = try directory.makeDirectory("home")
        try directory.makeFile("home/Projects/web/package.json", size: 10)
        let modules = try directory.makeDirectory("home/Projects/web/node_modules")
        try directory.makeFile("home/Projects/web/node_modules/a.js", size: 100)

        let target = CacheTarget(artifact: ArtifactDirectory(url: modules, kind: .nodeModules, size: 100))
        let cleaner = CacheCleaner(deletionGuard: DeletionGuard(homeDirectory: home), sizeMode: .logical)
        let report = cleaner.clean([target])

        XCTAssertFalse(directory.exists("home/Projects/web/node_modules"))
        XCTAssertTrue(directory.exists("home/Projects/web/package.json"))
        XCTAssertEqual(report.removedItems, [modules])
        XCTAssertEqual(report.freedBytes, 100)
    }

    func testDeletionGuardBlocksLocationsOutsideHome() throws {
        let directory = try makeTemporaryDirectory()
        let home = try directory.makeDirectory("home")
        let outside = try directory.makeDirectory("elsewhere/cache")
        try directory.makeFile("elsewhere/cache/data", size: 10)

        let target = CacheTarget(id: "x", title: "X", detail: "", group: .system, safety: .safe, locations: [outside])
        let cleaner = CacheCleaner(deletionGuard: DeletionGuard(homeDirectory: home), sizeMode: .logical)
        let report = cleaner.clean([target])

        XCTAssertTrue(directory.exists("elsewhere/cache/data"))
        XCTAssertEqual(report.failures.count, 1)
        XCTAssertEqual(report.freedBytes, 0)
    }

    func testProjectFoldersInsideCatalogLocationsAreDropped() throws {
        let home = URL(fileURLWithPath: "/Users/example")
        let caches = CacheTarget(
            id: "caches", title: "Caches", detail: "", group: .system, safety: .safe,
            locations: [home.appendingPathComponent("Library/Caches")]
        )
        let artifact = CacheTarget(artifact: ArtifactDirectory(
            url: home.appendingPathComponent("Library/Caches/tool/node_modules"),
            kind: .nodeModules,
            size: 1
        ))
        let project = CacheTarget(artifact: ArtifactDirectory(
            url: home.appendingPathComponent("Projects/web/node_modules"),
            kind: .nodeModules,
            size: 1
        ))

        let resolved = CacheCatalog.resolveOverlaps([caches, artifact, project])
        XCTAssertEqual(resolved.map(\.id), ["caches", project.id])
    }

    func testWildcardMatching() {
        XCTAssertTrue(CacheCatalog.wildcardMatches("iOS DeviceSupport", pattern: "*DeviceSupport"))
        XCTAssertFalse(CacheCatalog.wildcardMatches("DeviceSupport Old", pattern: "*DeviceSupport"))
        XCTAssertTrue(CacheCatalog.wildcardMatches("abc", pattern: "a*c"))
        XCTAssertFalse(CacheCatalog.wildcardMatches("ab", pattern: "ab*b"))
        XCTAssertTrue(CacheCatalog.wildcardMatches("anything", pattern: "*"))
        XCTAssertTrue(CacheCatalog.wildcardMatches("exact", pattern: "exact"))
    }
}
