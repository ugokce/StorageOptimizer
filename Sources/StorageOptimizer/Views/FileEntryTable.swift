import StorageCore
import SwiftUI

/// A sortable, multi-select table of files with Finder and Trash actions.
@MainActor
struct FileEntryTable: View {
    @Environment(AppState.self) private var appState
    let entries: [FileEntry]

    @State private var sortOrder: [KeyPathComparator<FileEntry>] = [KeyPathComparator(\FileEntry.size, order: .reverse)]
    @State private var selection: Set<FileEntry.ID> = []
    @State private var pendingTrash: [URL] = []
    @State private var isConfirmingTrash = false

    var body: some View {
        VStack(spacing: 0) {
            table
            Divider()
            footer
        }
        .confirmationDialog(trashTitle, isPresented: $isConfirmingTrash, titleVisibility: .visible) {
            Button("Move to Trash", role: .destructive) {
                appState.moveToTrash(pendingTrash)
                selection.subtract(pendingTrash)
                pendingTrash = []
            }
            Button("Cancel", role: .cancel) {
                pendingTrash = []
            }
        } message: {
            Text("You can restore items from the Trash until it is emptied. The space is freed when you empty the Trash.")
        }
    }

    private var table: some View {
        Table(sortedEntries, selection: $selection, sortOrder: $sortOrder) {
            TableColumn("Name", value: \FileEntry.name) { entry in
                HStack(spacing: 6) {
                    FileIcon(url: entry.url)
                    Text(entry.name)
                        .lineLimit(1)
                        .truncationMode(.middle)
                }
                .help(entry.url.path)
            }
            .width(min: 160, ideal: 260)

            TableColumn("Size", value: \FileEntry.size) { entry in
                Text(ByteFormatting.string(entry.size))
                    .monospacedDigit()
            }
            .width(min: 70, ideal: 90)

            TableColumn("Kind", value: \FileEntry.kindName) { entry in
                Text(entry.kindName)
            }
            .width(min: 70, ideal: 100)

            TableColumn("Location", value: \FileEntry.displayLocation) { entry in
                Text(entry.displayLocation)
                    .lineLimit(1)
                    .truncationMode(.head)
                    .foregroundStyle(.secondary)
            }
            .width(min: 120, ideal: 240)

            TableColumn("Modified", value: \FileEntry.modificationSortDate) { entry in
                DateText(date: entry.modificationDate)
            }
            .width(min: 80, ideal: 100)

            TableColumn("Last Opened", value: \FileEntry.accessSortDate) { entry in
                DateText(date: entry.accessDate)
            }
            .width(min: 80, ideal: 100)
        }
        .contextMenu(forSelectionType: FileEntry.ID.self) { ids in
            menuItems(for: Array(ids))
        } primaryAction: { ids in
            FinderActions.reveal(Array(ids))
        }
    }

    private var footer: some View {
        HStack(spacing: 12) {
            Text("\(entries.count.formatted()) items, \(ByteFormatting.string(totalSize))")
                .foregroundStyle(.secondary)
                .lineLimit(1)
            if !selectedURLs.isEmpty {
                Text("\(selectedURLs.count.formatted()) selected, \(ByteFormatting.string(selectedSize))")
                    .lineLimit(1)
            }
            Spacer()
            Button("Show in Finder") {
                FinderActions.reveal(selectedURLs)
            }
            .disabled(selectedURLs.isEmpty)
            Button("Move to Trash…", role: .destructive) {
                requestTrash(selectedURLs)
            }
            .disabled(selectedURLs.isEmpty)
        }
        .font(.callout)
        .monospacedDigit()
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    @ViewBuilder
    private func menuItems(for urls: [URL]) -> some View {
        if !urls.isEmpty {
            Button("Show in Finder") {
                FinderActions.reveal(urls)
            }
            if urls.count == 1, let url = urls.first {
                Button("Open") {
                    FinderActions.open(url)
                }
            }
            Button(urls.count == 1 ? "Copy Path" : "Copy Paths") {
                FinderActions.copyPaths(urls)
            }
            Divider()
            Button("Move to Trash…", role: .destructive) {
                requestTrash(urls)
            }
        }
    }

    private var sortedEntries: [FileEntry] {
        entries.sorted(using: sortOrder)
    }

    private var selectedURLs: [URL] {
        entries.filter { selection.contains($0.id) }.map(\.url)
    }

    private var totalSize: Int64 {
        entries.reduce(0) { $0 + $1.size }
    }

    private var selectedSize: Int64 {
        entries.filter { selection.contains($0.id) }.reduce(0) { $0 + $1.size }
    }

    private var trashTitle: String {
        pendingTrash.count == 1
            ? "Move “\(pendingTrash[0].lastPathComponent)” to the Trash?"
            : "Move \(pendingTrash.count) items to the Trash?"
    }

    private func requestTrash(_ urls: [URL]) {
        guard !urls.isEmpty else { return }
        pendingTrash = urls
        isConfirmingTrash = true
    }
}
