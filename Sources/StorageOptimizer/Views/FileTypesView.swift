import StorageCore
import SwiftUI

/// Space grouped by kind of file, with the largest files and extensions of each kind.
@MainActor
struct FileTypesView: View {
    @Environment(AppState.self) private var appState
    @State private var selectedCategory: FileCategory?

    var body: some View {
        Group {
            if let result = appState.result {
                let summaries = result.categorySummaries
                HSplitView {
                    categoryList(summaries: summaries, total: result.totalSize)
                        .frame(minWidth: 220, idealWidth: 300, maxWidth: 380)
                    detail(result: result, category: selectedCategory ?? summaries.first?.category)
                        .frame(minWidth: 360)
                }
            } else {
                EmptyScanView()
            }
        }
        .navigationTitle("File Types")
    }

    private func categoryList(summaries: [CategorySummary], total: Int64) -> some View {
        List(selection: $selectedCategory) {
            ForEach(summaries) { summary in
                CategoryRow(summary: summary, total: total)
                    .tag(summary.category)
            }
        }
    }

    @ViewBuilder
    private func detail(result: ScanResult, category: FileCategory?) -> some View {
        if let category {
            let extensions = Array(result.extensionSummaries(for: category).prefix(12))
            VStack(alignment: .leading, spacing: 0) {
                VStack(alignment: .leading, spacing: 10) {
                    Label(category.displayName, systemImage: category.systemImage)
                        .font(.title2.weight(.semibold))
                    if !extensions.isEmpty {
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 8) {
                                ForEach(extensions) { item in
                                    ExtensionChip(summary: item)
                                }
                            }
                        }
                    }
                    Text("Largest \(category.displayName.lowercased())")
                        .font(.headline)
                }
                .padding(16)
                Divider()
                FileEntryTable(entries: result.categoryFiles[category] ?? [])
                    .id(category)
            }
        } else {
            ContentUnavailableView("No Files", systemImage: "doc", description: Text("The scanned folder contains no files."))
        }
    }
}

private struct CategoryRow: View {
    let summary: CategorySummary
    let total: Int64

    private var fraction: Double {
        total > 0 ? Double(summary.size) / Double(total) : 0
    }

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: summary.category.systemImage)
                .foregroundStyle(summary.category.color)
                .frame(width: 20)
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text(summary.category.displayName)
                    Spacer()
                    Text(ByteFormatting.string(summary.size))
                        .monospacedDigit()
                }
                SizeBar(fraction: fraction, color: summary.category.color)
                Text("\(summary.count.formatted()) items · \(fraction.formatted(.percent.precision(.fractionLength(1))))")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .monospacedDigit()
            }
        }
        .padding(.vertical, 4)
    }
}

private struct ExtensionChip: View {
    let summary: ExtensionSummary

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(summary.displayName)
                .font(.callout.weight(.medium))
            Text("\(ByteFormatting.string(summary.size)) · \(summary.count.formatted())")
                .font(.caption)
                .foregroundStyle(.secondary)
                .monospacedDigit()
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(Palette.track, in: RoundedRectangle(cornerRadius: 8))
    }
}
