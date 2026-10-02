import StorageCore
import SwiftUI

/// Drill-down browser of the scanned folder tree, largest items first.
@MainActor
struct FolderBrowserView: View {
    @Environment(AppState.self) private var appState
    @State private var selection: Set<FileNode.ID> = []
    @State private var pendingTrash: [URL] = []
    @State private var isConfirmingTrash = false

    var body: some View {
        Group {
            if let result = appState.result {
                let current = appState.folderPath.last ?? result.root
                VStack(spacing: 0) {
                    breadcrumb(root: result.root)
                    Divider()
                    List(selection: $selection) {
                        ForEach(current.children) { child in
                            FolderRow(node: child, parentSize: current.size)
                        }
                        if current.smallFilesCount > 0 {
                            SmallFilesRow(count: current.smallFilesCount, size: current.smallFilesSize, parentSize: current.size)
                        }
                    }
                    .contextMenu(forSelectionType: FileNode.ID.self) { ids in
                        menuItems(for: nodes(with: ids, in: current))
                    } primaryAction: { ids in
                        open(nodes(with: ids, in: current))
                    }
                    Divider()
                    footer(for: current)
                }
                .onChange(of: current.id) {
                    selection = []
                }
            } else {
                EmptyScanView()
            }
        }
        .navigationTitle("Folders")
        .confirmationDialog(trashTitle, isPresented: $isConfirmingTrash, titleVisibility: .visible) {
            Button("Move to Trash", role: .destructive) {
                appState.moveToTrash(pendingTrash)
                selection = []
                pendingTrash = []
            }
            Button("Cancel", role: .cancel) {
                pendingTrash = []
            }
        } message: {
            Text("You can restore items from the Trash until it is emptied. The space is freed when you empty the Trash.")
        }
    }

    private func breadcrumb(root: FileNode) -> some View {
        HStack(spacing: 8) {
            Button {
                appState.folderPath.removeLast()
            } label: {
                Image(systemName: "chevron.left")
            }
            .disabled(appState.folderPath.isEmpty)
            .help("Back to the enclosing folder")

            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 4) {
                    Button {
                        appState.folderPath = []
                    } label: {
                        Label(root.name, systemImage: "house")
                    }
                    .buttonStyle(.plain)

                    ForEach(Array(appState.folderPath.enumerated()), id: \.element.id) { index, node in
                        Image(systemName: "chevron.right")
                            .font(.caption)
                            .foregroundStyle(.tertiary)
                        Button(node.name) {
                            appState.folderPath = Array(appState.folderPath.prefix(index + 1))
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    private func footer(for current: FileNode) -> some View {
        let selected = nodes(with: selection, in: current)
        return HStack(spacing: 12) {
            Text("\(current.name): \(ByteFormatting.string(current.size)), \(current.fileCount.formatted()) files")
                .foregroundStyle(.secondary)
            if !selected.isEmpty {
                Text("\(selected.count.formatted()) selected, \(ByteFormatting.string(selected.reduce(0) { $0 + $1.size }))")
            }
            Spacer()
            Button("Show in Finder") {
                FinderActions.reveal(selected.isEmpty ? [current.url] : selected.map(\.url))
            }
            Button("Move to Trash…", role: .destructive) {
                requestTrash(selected.map(\.url))
            }
            .disabled(selected.isEmpty)
        }
        .font(.callout)
        .monospacedDigit()
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    @ViewBuilder
    private func menuItems(for nodes: [FileNode]) -> some View {
        if !nodes.isEmpty {
            if nodes.count == 1, let node = nodes.first, node.canDrillDown {
                Button("Open Folder") {
                    appState.folderPath.append(node)
                }
            }
            Button("Show in Finder") {
                FinderActions.reveal(nodes.map(\.url))
            }
            Button(nodes.count == 1 ? "Copy Path" : "Copy Paths") {
                FinderActions.copyPaths(nodes.map(\.url))
            }
            Divider()
            Button("Move to Trash…", role: .destructive) {
                requestTrash(nodes.map(\.url))
            }
        }
    }

    private func nodes(with ids: Set<FileNode.ID>, in folder: FileNode) -> [FileNode] {
        folder.children.filter { ids.contains($0.id) }
    }

    /// Double-click: open folders, reveal files.
    private func open(_ nodes: [FileNode]) {
        guard let node = nodes.first else { return }
        if nodes.count == 1 && node.canDrillDown {
            appState.folderPath.append(node)
        } else {
            FinderActions.reveal(nodes.map(\.url))
        }
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

private struct FolderRow: View {
    let node: FileNode
    let parentSize: Int64

    private var fraction: Double {
        parentSize > 0 ? Double(node.size) / Double(parentSize) : 0
    }

    var body: some View {
        HStack(spacing: 10) {
            FileIcon(url: node.url, size: 20)
            VStack(alignment: .leading, spacing: 4) {
                Text(node.name)
                    .lineLimit(1)
                    .truncationMode(.middle)
                SizeBar(fraction: fraction, color: node.isDirectory && !node.isPackage ? .accentColor : node.category.color)
            }
            Text(fraction.formatted(.percent.precision(.fractionLength(1))))
                .foregroundStyle(.secondary)
                .frame(width: 56, alignment: .trailing)
            Text(ByteFormatting.string(node.size))
                .frame(width: 80, alignment: .trailing)
            Image(systemName: "chevron.right")
                .foregroundStyle(.tertiary)
                .opacity(node.canDrillDown ? 1 : 0)
        }
        .monospacedDigit()
        .padding(.vertical, 2)
        .help(node.url.path)
    }
}

private struct SmallFilesRow: View {
    let count: Int
    let size: Int64
    let parentSize: Int64

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "doc.on.doc")
                .frame(width: 20)
                .foregroundStyle(.secondary)
            VStack(alignment: .leading, spacing: 4) {
                Text("\(count.formatted()) smaller files")
                    .foregroundStyle(.secondary)
                SizeBar(fraction: parentSize > 0 ? Double(size) / Double(parentSize) : 0, color: .secondary)
            }
            Text(ByteFormatting.string(size))
                .foregroundStyle(.secondary)
                .frame(width: 80, alignment: .trailing)
            Image(systemName: "chevron.right")
                .opacity(0)
        }
        .monospacedDigit()
        .padding(.vertical, 2)
        .help("Files under 1 MB are grouped together.")
    }
}
