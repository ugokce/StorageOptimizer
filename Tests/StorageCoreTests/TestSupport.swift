import Foundation
import XCTest

/// A temporary folder for file system fixtures.
struct TemporaryDirectory {
    let url: URL

    fileprivate init() throws {
        url = FileManager.default.temporaryDirectory
            .appendingPathComponent("StorageCoreTests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
    }

    /// Creates a file of `size` bytes at `relativePath`, creating intermediate folders.
    @discardableResult
    func makeFile(_ relativePath: String, size: Int) throws -> URL {
        let fileURL = url.appendingPathComponent(relativePath)
        try FileManager.default.createDirectory(
            at: fileURL.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        try Data(count: size).write(to: fileURL)
        return fileURL
    }

    @discardableResult
    func makeDirectory(_ relativePath: String) throws -> URL {
        let directoryURL = url.appendingPathComponent(relativePath, isDirectory: true)
        try FileManager.default.createDirectory(at: directoryURL, withIntermediateDirectories: true)
        return directoryURL
    }

    func exists(_ relativePath: String) -> Bool {
        FileManager.default.fileExists(atPath: url.appendingPathComponent(relativePath).path)
    }
}

extension XCTestCase {
    /// Creates a temporary folder that is deleted when the test finishes.
    func makeTemporaryDirectory() throws -> TemporaryDirectory {
        let directory = try TemporaryDirectory()
        let url = directory.url
        addTeardownBlock {
            try? FileManager.default.removeItem(at: url)
        }
        return directory
    }
}
