import StorageCore
import SwiftUI

enum SizeFilter: Int64, CaseIterable, Identifiable {
    case any = 0
    case mb10 = 10_000_000
    case mb100 = 100_000_000
    case mb500 = 500_000_000
    case gb1 = 1_000_000_000

    var id: Int64 { rawValue }

    var title: String {
        switch self {
        case .any: "Any size"
        case .mb10: "10 MB+"
        case .mb100: "100 MB+"
        case .mb500: "500 MB+"
        case .gb1: "1 GB+"
        }
    }
}

@MainActor
struct LargeFilesView: View {
    @Environment(AppState.self) private var appState
    @State private var minimumSize: SizeFilter = .mb100
    @State private var category: FileCategory?
    @State private var searchText = ""
    @State private var onlyNotOpenedRecently = false

    var body: some View {
        Group {
            if let result = appState.result {
                VStack(spacing: 0) {
                    filterBar
                    Divider()
                    FileEntryTable(entries: filtered(result.largestFiles))
                }
            } else {
                EmptyScanView()
            }
        }
        .navigationTitle("Large Files")
        .searchable(text: $searchText, prompt: "Filter by name or folder")
    }

    /// One row when the window is wide enough, two rows otherwise.
    private var filterBar: some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: 16) {
                sizePicker
                typePicker
                notOpenedToggle
                Spacer(minLength: 0)
            }
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    sizePicker
                    Spacer(minLength: 0)
                }
                HStack(spacing: 16) {
                    typePicker
                    notOpenedToggle
                    Spacer(minLength: 0)
                }
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    private var sizePicker: some View {
        Picker("Size", selection: $minimumSize) {
            ForEach(SizeFilter.allCases) { filter in
                Text(filter.title).tag(filter)
            }
        }
        .pickerStyle(.segmented)
        .fixedSize()
    }

    private var typePicker: some View {
        Picker("Type", selection: $category) {
            Text("All Types").tag(FileCategory?.none)
            Divider()
            ForEach(FileCategory.allCases) { category in
                Label(category.displayName, systemImage: category.systemImage)
                    .tag(FileCategory?.some(category))
            }
        }
        .fixedSize()
    }

    private var notOpenedToggle: some View {
        Toggle("Not opened in 6 months", isOn: $onlyNotOpenedRecently)
            .fixedSize()
    }

    private func filtered(_ files: [FileEntry]) -> [FileEntry] {
        let cutoff = Calendar.current.date(byAdding: .month, value: -6, to: Date()) ?? .distantPast
        let query = searchText.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        return files.filter { entry in
            if entry.size < minimumSize.rawValue { return false }
            if let category, entry.category != category { return false }
            if onlyNotOpenedRecently {
                let lastUse = entry.accessDate ?? entry.modificationDate ?? .distantPast
                if lastUse > cutoff { return false }
            }
            if !query.isEmpty && !entry.url.path.lowercased().contains(query) { return false }
            return true
        }
    }
}
