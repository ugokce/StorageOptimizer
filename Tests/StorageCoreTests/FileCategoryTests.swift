import XCTest
@testable import StorageCore

final class FileCategoryTests: XCTestCase {
    func testKnownExtensions() {
        XCTAssertEqual(FileCategory.category(forExtension: "mp4"), .video)
        XCTAssertEqual(FileCategory.category(forExtension: "MOV"), .video)
        XCTAssertEqual(FileCategory.category(forExtension: "heic"), .image)
        XCTAssertEqual(FileCategory.category(forExtension: "flac"), .audio)
        XCTAssertEqual(FileCategory.category(forExtension: "pdf"), .document)
        XCTAssertEqual(FileCategory.category(forExtension: "zip"), .archive)
        XCTAssertEqual(FileCategory.category(forExtension: "dmg"), .diskImage)
        XCTAssertEqual(FileCategory.category(forExtension: "app"), .application)
        XCTAssertEqual(FileCategory.category(forExtension: "swift"), .developer)
        XCTAssertEqual(FileCategory.category(forExtension: "photoslibrary"), .image)
    }

    func testUnknownOrMissingExtension() {
        XCTAssertEqual(FileCategory.category(forExtension: ""), .other)
        XCTAssertEqual(FileCategory.category(forExtension: "qwertyunknownext"), .other)
    }

    func testResolverCachesResults() {
        var resolver = CategoryResolver()
        XCTAssertEqual(resolver.category(forExtension: "jpg"), .image)
        XCTAssertEqual(resolver.category(forExtension: "jpg"), .image)
    }
}
