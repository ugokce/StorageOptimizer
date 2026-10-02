import AppKit
import SwiftUI
import XCTest
@testable import StorageOptimizer

/// macOS measures the smallest size of each window column with very narrow widths. Text that
/// refuses to truncate then reports thousands of points of height, the column's minimum height
/// becomes taller than any window, and the window content is pushed up behind the toolbar.
@MainActor
final class LayoutTests: XCTestCase {
    func testFullDiskAccessBannerHasBoundedHeightWhenNarrow() {
        let host = NSHostingController(rootView: FullDiskAccessBanner())
        let size = host.sizeThatFits(in: CGSize(width: 160, height: 10_000))
        XCTAssertLessThan(size.height, 200, "Banner height at a narrow width: \(size.height)")
    }

    func testCacheCleanerHasBoundedMinimumHeightWhenNarrow() {
        let host = NSHostingController(rootView: CacheCleanerView().environment(AppState()))
        let size = host.sizeThatFits(in: CGSize(width: 220, height: 0))
        XCTAssertLessThan(size.height, 600, "Cache Cleaner minimum height at a narrow width: \(size.height)")
    }
}
