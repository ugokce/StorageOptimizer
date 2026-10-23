import AppKit
import StorageCore
import SwiftUI

/// A rounded panel with a title, used on the Overview.
struct Card<Content: View>: View {
    let title: String
    var systemImage: String?
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let systemImage {
                Label(title, systemImage: systemImage)
                    .font(.headline)
            } else {
                Text(title)
                    .font(.headline)
            }
            content
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 12))
        .overlay {
            RoundedRectangle(cornerRadius: 12)
                .strokeBorder(Color.primary.opacity(0.08))
        }
    }
}

/// The Finder icon of a file or folder.
struct FileIcon: View {
    let url: URL
    var size: CGFloat = 16

    var body: some View {
        Image(nsImage: FinderActions.icon(for: url))
            .resizable()
            .interpolation(.high)
            .frame(width: size, height: size)
    }
}

/// A thin horizontal bar showing a fraction.
struct SizeBar: View {
    let fraction: Double
    var color: Color = .accentColor

    var body: some View {
        GeometryReader { proxy in
            ZStack(alignment: .leading) {
                Capsule()
                    .fill(Palette.track)
                Capsule()
                    .fill(color)
                    .frame(width: max(3, proxy.size.width * min(max(fraction, 0), 1)))
            }
        }
        .frame(height: 6)
    }
}

/// A date, or a dash when unknown.
struct DateText: View {
    let date: Date?

    var body: some View {
        if let date {
            Text(date, format: .dateTime.year().month(.abbreviated).day())
        } else {
            Text("—")
                .foregroundStyle(.secondary)
        }
    }
}

/// One colored segment of a `UsageBar`.
struct UsageSegment: Identifiable {
    let id: String
    let label: String
    let size: Int64
    let color: Color
}

/// A stacked bar like the one in System Settings › Storage.
struct UsageBar: View {
    let segments: [UsageSegment]
    let total: Int64

    var body: some View {
        GeometryReader { proxy in
            HStack(spacing: 2) {
                ForEach(segments) { segment in
                    Rectangle()
                        .fill(segment.color)
                        .frame(width: width(of: segment, in: proxy.size.width))
                        .help("\(segment.label): \(ByteFormatting.string(segment.size))")
                }
                Spacer(minLength: 0)
            }
            .frame(width: proxy.size.width, height: proxy.size.height, alignment: .leading)
            .background(Palette.track)
            .clipShape(RoundedRectangle(cornerRadius: 6))
        }
        .frame(height: 24)
    }

    private func width(of segment: UsageSegment, in fullWidth: CGFloat) -> CGFloat {
        guard total > 0, segment.size > 0 else { return 0 }
        let gaps = CGFloat(max(segments.count - 1, 0)) * 2
        let available = max(fullWidth - gaps, 0)
        return max(2, available * CGFloat(Double(segment.size) / Double(total)))
    }
}

/// A legend entry: color dot, label and value.
struct LegendItem: View {
    let color: Color
    let label: String
    let value: String

    var body: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(color)
                .frame(width: 9, height: 9)
            Text(label)
            Text(value)
                .foregroundStyle(.secondary)
                .monospacedDigit()
        }
        .font(.callout)
        .lineLimit(1)
    }
}

/// Shown in place of scan-based views before the first scan.
struct EmptyScanView: View {
    @Environment(AppState.self) private var appState

    var body: some View {
        if appState.isScanning {
            VStack(spacing: 12) {
                ProgressView()
                Text("Scanning \(appState.scanTarget.title)…")
                    .font(.headline)
                Text("\(appState.progress.filesScanned.formatted()) files · \(ByteFormatting.string(appState.progress.bytesScanned))")
                    .foregroundStyle(.secondary)
                    .monospacedDigit()
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else {
            ContentUnavailableView {
                Label("No Scan Yet", systemImage: "internaldrive")
            } description: {
                Text("Scan your \(appState.scanTarget.title.lowercased()) to see which files and folders take up space.")
            } actions: {
                Button("Scan \(appState.scanTarget.title)") {
                    appState.startScan()
                }
                .buttonStyle(.borderedProminent)
            }
        }
    }
}

/// Progress strip shown at the top of the window while scanning.
struct ScanStatusBar: View {
    @Environment(AppState.self) private var appState

    var body: some View {
        HStack(spacing: 10) {
            ProgressView()
                .controlSize(.small)
            Text("Scanning… \(appState.progress.filesScanned.formatted()) files, \(ByteFormatting.string(appState.progress.bytesScanned))")
                .monospacedDigit()
            Text(appState.progress.currentPath)
                .lineLimit(1)
                .truncationMode(.middle)
                .foregroundStyle(.secondary)
            Spacer()
            Button("Stop") {
                appState.cancelScan()
            }
        }
        .font(.callout)
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .background(.bar)
    }
}

/// Explains why some folders are unreadable and links to the setting that fixes it.
struct FullDiskAccessBanner: View {
    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: "lock.shield")
                .font(.title2)
                .foregroundStyle(.orange)
            // This bar sits outside any scroll view, so its text must truncate rather than grow:
            // text that never truncates gets very tall at narrow widths and pushes the whole
            // window content up behind the toolbar.
            VStack(alignment: .leading, spacing: 2) {
                Text("Full Disk Access is off")
                    .font(.headline)
                    .lineLimit(1)
                Text("Some folders, such as Mail, Messages and other apps' data, can't be measured or cleaned. Turn on Full Disk Access for Storage Optimizer (or for Terminal when using swift run), then reopen the app.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .lineLimit(3)
            }
            .layoutPriority(1)
            Spacer(minLength: 0)
            Button("Open Settings") {
                FullDiskAccess.openSettings()
            }
        }
        .padding(12)
        .background(Color.orange.opacity(0.1))
    }
}
