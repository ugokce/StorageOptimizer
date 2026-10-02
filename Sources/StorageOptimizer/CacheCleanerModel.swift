import Foundation
import Observation
import StorageCore

/// State of the Cache Cleaner: which targets exist, their sizes and the user's selection.
@MainActor
@Observable
final class CacheCleanerModel {
    struct Row: Identifiable {
        let target: CacheTarget
        var size: Int64?
        var isMeasuring: Bool
        var isSelected: Bool

        var id: String { target.id }
    }

    private(set) var rows: [Row] = []
    private(set) var isRefreshing = false
    private(set) var isCleaning = false
    /// The result of the last cleaning run; shown as a sheet.
    var report: CleanReport?

    /// Called with the project folders a cleaning run deleted, so the scan result can be updated.
    @ObservationIgnored var onItemsRemoved: (@MainActor ([URL]) -> Void)?
    @ObservationIgnored private var projectArtifacts: [ArtifactDirectory] = []

    var selectedRows: [Row] { rows.filter(\.isSelected) }

    var selectedSize: Int64 {
        selectedRows.reduce(0) { $0 + ($1.size ?? 0) }
    }

    /// Total size of the targets that are safe to clean, excluding project folders.
    var recommendedSize: Int64 {
        rows.filter { $0.target.isRecommended }.reduce(0) { $0 + ($1.size ?? 0) }
    }

    var isMeasuringAnything: Bool {
        rows.contains { $0.isMeasuring }
    }

    func rows(in group: CacheGroup) -> [Row] {
        rows.filter { $0.target.group == group }
    }

    // MARK: - Measuring

    /// Rediscovers cache locations and measures them in the background.
    func refresh() {
        guard !isRefreshing, !isCleaning else { return }
        isRefreshing = true

        Task { [weak self] in
            let catalogTargets = await Task.detached(priority: .utility) {
                CacheCatalog.availableTargets()
            }.value
            guard let self else { return }
            self.applyTargets(catalogTargets: catalogTargets, remeasure: true)

            let measurable = self.rows.map(\.target).filter { $0.reportsSize && $0.group != .projects }
            let cleaner = CacheCleaner()
            await withTaskGroup(of: (String, Int64).self) { group in
                for target in measurable {
                    group.addTask {
                        (target.id, cleaner.measure(target))
                    }
                }
                for await (id, size) in group {
                    await self.updateSize(id: id, size: size)
                }
            }
            self.isRefreshing = false
        }
    }

    /// Adds the rebuildable project folders found by the latest scan.
    func setProjectArtifacts(_ artifacts: [ArtifactDirectory]) {
        projectArtifacts = artifacts
        let catalogTargets = rows.map(\.target).filter { $0.group != .projects }
        applyTargets(catalogTargets: catalogTargets, remeasure: false)
    }

    private func applyTargets(catalogTargets: [CacheTarget], remeasure: Bool) {
        let artifactSizes = Dictionary(
            projectArtifacts.map { (CacheTarget.artifactID(for: $0.url), $0.size) },
            uniquingKeysWith: { first, _ in first }
        )
        let targets = CacheCatalog.resolveOverlaps(catalogTargets + projectArtifacts.map(CacheTarget.init(artifact:)))
        let previous = Dictionary(rows.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })

        rows = targets.map { target in
            let old = previous[target.id]
            let measurable = target.reportsSize && target.group != .projects
            return Row(
                target: target,
                size: target.group == .projects ? artifactSizes[target.id] : old?.size,
                isMeasuring: measurable && (remeasure || old?.isMeasuring == true),
                isSelected: old?.isSelected ?? target.isRecommended
            )
        }
    }

    private func updateSize(id: String, size: Int64) {
        guard let index = rows.firstIndex(where: { $0.id == id }) else { return }
        rows[index].size = size
        rows[index].isMeasuring = false
    }

    // MARK: - Selection

    func toggle(_ id: String) {
        guard let index = rows.firstIndex(where: { $0.id == id }) else { return }
        rows[index].isSelected.toggle()
    }

    func selectRecommended() {
        for index in rows.indices {
            rows[index].isSelected = rows[index].target.isRecommended
        }
    }

    func deselectAll() {
        for index in rows.indices {
            rows[index].isSelected = false
        }
    }

    // MARK: - Cleaning

    func cleanSelected() {
        let targets = selectedRows.map(\.target)
        guard !targets.isEmpty, !isCleaning else { return }
        isCleaning = true

        Task { [weak self] in
            let report = await Task.detached(priority: .userInitiated) {
                CacheCleaner().clean(targets)
            }.value
            guard let self else { return }
            self.isCleaning = false
            self.report = report

            if !report.removedItems.isEmpty {
                let removed = Set(report.removedItems.map { PathUtilities.canonicalPath($0) })
                self.projectArtifacts.removeAll { removed.contains(PathUtilities.canonicalPath($0.url)) }
                self.onItemsRemoved?(report.removedItems)
            }
            self.refresh()
        }
    }
}
