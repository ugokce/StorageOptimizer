import Charts
import StorageCore
import SwiftUI

@MainActor
struct OverviewView: View {
    @Environment(AppState.self) private var appState

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                if let volume = appState.volume {
                    DiskUsageCard(volume: volume, result: appState.result, scanTarget: appState.scanTarget)
                }

                HStack(alignment: .top, spacing: 16) {
                    CacheSummaryCard()
                    ScanSummaryCard()
                }

                if let result = appState.result {
                    CategoryChartCard(summaries: result.categorySummaries)
                    HStack(alignment: .top, spacing: 16) {
                        TopFoldersCard(root: result.root)
                        TopFilesCard(files: Array(result.largestFiles.prefix(8)))
                    }
                } else {
                    EmptyScanView()
                        .frame(height: 280)
                }
            }
            .padding(20)
        }
        .navigationTitle("Overview")
    }
}

/// Disk capacity with the scanned categories laid out like the macOS Storage bar.
private struct DiskUsageCard: View {
    let volume: VolumeInfo
    let result: ScanResult?
    let scanTarget: ScanTarget

    var body: some View {
        Card(title: volume.name, systemImage: "internaldrive") {
            HStack(alignment: .firstTextBaseline) {
                Text("\(ByteFormatting.string(volume.usedCapacity)) of \(ByteFormatting.string(volume.totalCapacity)) used")
                    .font(.title3.weight(.semibold))
                    .monospacedDigit()
                Spacer()
                Text("\(ByteFormatting.string(volume.availableCapacity)) available")
                    .foregroundStyle(volume.usedFraction > 0.9 ? Color.red : Color.secondary)
                    .monospacedDigit()
            }

            UsageBar(segments: segments, total: volume.totalCapacity)

            LazyVGrid(columns: [GridItem(.adaptive(minimum: 170), alignment: .leading)], alignment: .leading, spacing: 6) {
                ForEach(segments) { segment in
                    LegendItem(color: segment.color, label: segment.label, value: ByteFormatting.string(segment.size))
                }
            }

            if volume.usedFraction > 0.9 {
                Label(
                    "Your disk is almost full. Check Large Files and the Cache Cleaner, then empty the Trash to get space back.",
                    systemImage: "exclamationmark.triangle.fill"
                )
                .font(.callout)
                .foregroundStyle(.secondary)
            }
        }
    }

    /// Scanned categories in their fixed order, followed by everything the scan didn't cover.
    private var segments: [UsageSegment] {
        var segments: [UsageSegment] = []
        var accounted: Int64 = 0
        if let result {
            let stats = result.categoryStats
            for category in FileCategory.allCases {
                guard let size = stats[category]?.size, size > 0 else { continue }
                segments.append(UsageSegment(id: category.id, label: category.displayName, size: size, color: category.color))
                accounted += size
            }
        }
        let remainder = max(0, volume.usedCapacity - accounted)
        let remainderLabel: String
        if result == nil {
            remainderLabel = "Used"
        } else if scanTarget == .entireDisk {
            remainderLabel = "System & unreadable"
        } else {
            remainderLabel = "Outside \(scanTarget.title)"
        }
        segments.append(UsageSegment(id: "remainder", label: remainderLabel, size: remainder, color: Palette.unscanned))
        return segments
    }
}

private struct CacheSummaryCard: View {
    @Environment(AppState.self) private var appState

    var body: some View {
        let model = appState.cacheModel
        Card(title: "Reclaimable Caches", systemImage: "sparkles") {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(ByteFormatting.string(model.recommendedSize))
                    .font(.system(size: 28, weight: .semibold))
                    .monospacedDigit()
                if model.isMeasuringAnything {
                    ProgressView()
                        .controlSize(.small)
                }
            }
            Text("Xcode DerivedData, package manager downloads, app caches and logs that are recreated when needed.")
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Button("Open Cache Cleaner") {
                appState.selection = .cacheCleaner
            }
        }
    }
}

private struct ScanSummaryCard: View {
    @Environment(AppState.self) private var appState

    var body: some View {
        Card(title: "Last Scan", systemImage: "magnifyingglass") {
            if let result = appState.result {
                Text(ByteFormatting.string(result.totalSize))
                    .font(.system(size: 28, weight: .semibold))
                    .monospacedDigit()
                VStack(alignment: .leading, spacing: 4) {
                    Text(result.root.url.abbreviatedPath)
                        .lineLimit(1)
                        .truncationMode(.middle)
                    Text("\(result.fileCount.formatted()) files · scanned in \(Duration.seconds(result.duration).formatted(.units(allowed: [.minutes, .seconds], width: .abbreviated)))")
                    if result.inaccessibleCount > 0 {
                        Text("\(result.inaccessibleCount.formatted()) folders couldn't be read")
                    }
                }
                .font(.callout)
                .foregroundStyle(.secondary)
            } else {
                Text(statusText)
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var statusText: String {
        switch appState.phase {
        case .scanning: "Scanning \(appState.scanTarget.title)…"
        case .cancelled: "The last scan was stopped."
        case .failed(let message): "The last scan failed: \(message)"
        case .idle, .finished: "Not scanned yet."
        }
    }
}

/// Size per file type. Bars carry the category color; the axis names each one.
private struct CategoryChartCard: View {
    let summaries: [CategorySummary]

    var body: some View {
        Card(title: "Space by File Type", systemImage: "chart.bar.xaxis") {
            if summaries.isEmpty {
                Text("No files found.")
                    .foregroundStyle(.secondary)
            } else {
                Chart(summaries) { summary in
                    BarMark(
                        x: .value("Size", Double(summary.size)),
                        y: .value("Type", summary.category.displayName)
                    )
                    .foregroundStyle(summary.category.color)
                    .cornerRadius(4)
                    .annotation(position: .trailing, alignment: .leading) {
                        Text(ByteFormatting.string(summary.size))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .monospacedDigit()
                    }
                }
                .chartXAxis(.hidden)
                .chartYAxis {
                    AxisMarks { _ in
                        AxisValueLabel()
                    }
                }
                .frame(height: CGFloat(summaries.count) * 30 + 10)
                .padding(.trailing, 70)
            }
        }
    }
}

private struct TopFoldersCard: View {
    @Environment(AppState.self) private var appState
    let root: FileNode

    var body: some View {
        Card(title: "Largest Items in \(root.name)", systemImage: "folder") {
            ForEach(Array(root.children.prefix(8))) { node in
                Button {
                    if node.canDrillDown {
                        appState.showInFolders(node)
                    } else {
                        FinderActions.reveal([node.url])
                    }
                } label: {
                    HStack(spacing: 8) {
                        FileIcon(url: node.url, size: 18)
                        VStack(alignment: .leading, spacing: 3) {
                            Text(node.name)
                                .lineLimit(1)
                                .truncationMode(.middle)
                            SizeBar(fraction: root.size > 0 ? Double(node.size) / Double(root.size) : 0)
                        }
                        Text(ByteFormatting.string(node.size))
                            .monospacedDigit()
                            .foregroundStyle(.secondary)
                            .frame(width: 80, alignment: .trailing)
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            Button("Browse Folders") {
                appState.selection = .folders
            }
        }
    }
}

private struct TopFilesCard: View {
    @Environment(AppState.self) private var appState
    let files: [FileEntry]

    var body: some View {
        Card(title: "Largest Files", systemImage: "doc.text.magnifyingglass") {
            ForEach(files) { file in
                HStack(spacing: 8) {
                    FileIcon(url: file.url, size: 18)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(file.name)
                            .lineLimit(1)
                            .truncationMode(.middle)
                        Text(file.displayLocation)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .truncationMode(.head)
                    }
                    Spacer(minLength: 8)
                    Text(ByteFormatting.string(file.size))
                        .monospacedDigit()
                        .foregroundStyle(.secondary)
                    Button {
                        FinderActions.reveal([file.url])
                    } label: {
                        Image(systemName: "magnifyingglass")
                    }
                    .buttonStyle(.borderless)
                    .help("Show in Finder")
                }
            }
            Button("Show All Large Files") {
                appState.selection = .largeFiles
            }
        }
    }
}
