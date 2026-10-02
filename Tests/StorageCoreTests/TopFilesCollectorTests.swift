import XCTest
@testable import StorageCore

final class TopFilesCollectorTests: XCTestCase {
    private func entry(_ size: Int64) -> FileEntry {
        FileEntry(url: URL(fileURLWithPath: "/tmp/file-\(size)"), size: size, category: .other)
    }

    func testKeepsLargestEntries() {
        var collector = TopFilesCollector(capacity: 10)
        for size in Array(1...100).shuffled() {
            collector.insert(entry(Int64(size)))
        }
        XCTAssertEqual(collector.count, 10)
        XCTAssertEqual(collector.sortedDescending().map(\.size), (91...100).reversed().map { Int64($0) })
    }

    func testAcceptsReflectsCurrentMinimum() {
        var collector = TopFilesCollector(capacity: 2)
        XCTAssertTrue(collector.accepts(1))
        collector.insert(entry(10))
        collector.insert(entry(20))
        XCTAssertFalse(collector.accepts(5))
        XCTAssertTrue(collector.accepts(15))
    }

    func testMerge() {
        var first = TopFilesCollector(capacity: 3)
        var second = TopFilesCollector(capacity: 3)
        for size: Int64 in [1, 5, 9] {
            first.insert(entry(size))
        }
        for size: Int64 in [2, 7, 8] {
            second.insert(entry(size))
        }
        first.merge(second)
        XCTAssertEqual(first.sortedDescending().map(\.size), [9, 8, 7])
    }

    func testZeroCapacityKeepsNothing() {
        var collector = TopFilesCollector(capacity: 0)
        collector.insert(entry(1))
        XCTAssertEqual(collector.count, 0)
        XCTAssertFalse(collector.accepts(100))
    }
}
