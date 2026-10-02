import XCTest
@testable import StorageCore

final class DeletionGuardTests: XCTestCase {
    func testAllowsOnlySafeLocations() throws {
        let directory = try makeTemporaryDirectory()
        let home = try directory.makeDirectory("home")
        try directory.makeDirectory("home/Documents")
        try directory.makeDirectory("home/Library/Caches/com.example")
        try directory.makeFile("home/Downloads/big.mov", size: 1)
        let deletionGuard = DeletionGuard(homeDirectory: home)

        XCTAssertFalse(deletionGuard.canDelete(URL(fileURLWithPath: "/")))
        XCTAssertFalse(deletionGuard.canDelete(URL(fileURLWithPath: "/System/Library/CoreServices")))
        XCTAssertFalse(deletionGuard.canDelete(URL(fileURLWithPath: "/usr/bin/swift")))
        XCTAssertFalse(deletionGuard.canDelete(home))
        XCTAssertFalse(deletionGuard.canDelete(home.appendingPathComponent("Documents")))
        XCTAssertFalse(deletionGuard.canDelete(home.appendingPathComponent("Library/Caches")))
        XCTAssertFalse(deletionGuard.canDelete(home.appendingPathComponent("Library/Keychains/login.keychain-db")))
        XCTAssertFalse(deletionGuard.canDelete(URL(fileURLWithPath: "/Applications")))
        XCTAssertFalse(deletionGuard.canDelete(URL(fileURLWithPath: "/Volumes/External")))

        XCTAssertTrue(deletionGuard.canDelete(home.appendingPathComponent("Library/Caches/com.example")))
        XCTAssertTrue(deletionGuard.canDelete(home.appendingPathComponent("Downloads/big.mov")))
        XCTAssertTrue(deletionGuard.canDelete(URL(fileURLWithPath: "/Applications/Example.app")))
        XCTAssertTrue(deletionGuard.canDelete(URL(fileURLWithPath: "/Volumes/External/movie.mov")))
    }

    func testRejectsItemsOutsideHome() throws {
        let directory = try makeTemporaryDirectory()
        let home = try directory.makeDirectory("home")
        let outside = try directory.makeFile("elsewhere/file.txt", size: 1)

        XCTAssertThrowsError(try DeletionGuard(homeDirectory: home).validate(outside)) { error in
            guard case .outsideAllowedLocations = error as? DeletionError else {
                return XCTFail("Unexpected error: \(error)")
            }
        }
    }

    func testResolvesSymbolicLinksInParentFolders() throws {
        let directory = try makeTemporaryDirectory()
        let home = try directory.makeDirectory("home")
        let link = home.appendingPathComponent("system-link")
        try FileManager.default.createSymbolicLink(at: link, withDestinationURL: URL(fileURLWithPath: "/System"))
        let deletionGuard = DeletionGuard(homeDirectory: home)

        // Paths through the link point into /System and must be refused...
        XCTAssertFalse(deletionGuard.canDelete(link.appendingPathComponent("Library")))
        // ...but the link itself lives in the home folder and may be removed.
        XCTAssertTrue(deletionGuard.canDelete(link))
    }
}
