import StorageCore
import SwiftUI

@MainActor
struct CacheCleanerView: View {
    @Environment(AppState.self) private var appState
    @State private var isConfirming = false

    var body: some View {
        @Bindable var model = appState.cacheModel

        VStack(spacing: 0) {
            header
            if RunningApps.isXcodeRunning && model.selectedRows.contains(where: { $0.target.group == .developer }) {
                xcodeWarning
            }
            Divider()
            List {
                ForEach(CacheGroup.allCases) { group in
                    let rows = model.rows(in: group)
                    if !rows.isEmpty {
                        Section {
                            ForEach(rows) { row in
                                CacheRowView(row: row) {
                                    model.toggle(row.id)
                                }
                            }
                        } header: {
                            Text(group.title)
                        } footer: {
                            if group == .projects {
                                Text("Found by the last scan. Delete these only for projects you are not working on right now.")
                            }
                        }
                    }
                }
                if model.rows.isEmpty {
                    Text(model.isRefreshing ? "Looking for caches…" : "No cache folders found.")
                        .foregroundStyle(.secondary)
                }
            }
            Divider()
            footer
        }
        .navigationTitle("Cache Cleaner")
        .alert("Delete the selected caches?", isPresented: $isConfirming) {
            Button("Delete Permanently", role: .destructive) {
                model.cleanSelected()
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(confirmationMessage)
        }
        .sheet(item: $model.report) { report in
            CleanReportView(report: report)
        }
    }

    private var header: some View {
        let model = appState.cacheModel
        return HStack(alignment: .top, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Caches are rebuilt automatically when apps and tools need them.")
                    .font(.headline)
                Text("Cleaning deletes them permanently, so the space is freed right away. Items marked Review First may hold something you want to keep.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
            Button("Select Recommended") {
                model.selectRecommended()
            }
            Button("Deselect All") {
                model.deselectAll()
            }
            Button {
                model.refresh()
            } label: {
                Label("Refresh", systemImage: "arrow.clockwise")
            }
            .disabled(model.isRefreshing || model.isCleaning)
        }
        .padding(16)
    }

    private var xcodeWarning: some View {
        Label("Xcode is running. Quit Xcode before cleaning its caches so it doesn't recreate them mid-build.", systemImage: "exclamationmark.triangle.fill")
            .font(.callout)
            .padding(.horizontal, 16)
            .padding(.vertical, 8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.orange.opacity(0.1))
    }

    private var footer: some View {
        let model = appState.cacheModel
        return HStack(spacing: 12) {
            if model.isCleaning {
                ProgressView()
                    .controlSize(.small)
                Text("Cleaning…")
            } else {
                Text("\(model.selectedRows.count) selected · \(ByteFormatting.string(model.selectedSize))")
                    .monospacedDigit()
            }
            Spacer()
            Button("Clean Selected…") {
                isConfirming = true
            }
            .buttonStyle(.borderedProminent)
            .disabled(model.selectedRows.isEmpty || model.isCleaning)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
    }

    private var confirmationMessage: String {
        let model = appState.cacheModel
        var message = "This permanently deletes about \(ByteFormatting.string(model.selectedSize)) from \(model.selectedRows.count) locations. It can't be undone."
        if model.selectedRows.contains(where: { $0.target.safety == .caution }) {
            message += "\n\nSome selected items are marked Review First."
        }
        return message
    }
}

private struct CacheRowView: View {
    let row: CacheCleanerModel.Row
    let onToggle: () -> Void

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Button(action: onToggle) {
                Image(systemName: row.isSelected ? "checkmark.square.fill" : "square")
                    .font(.title3)
                    .foregroundStyle(row.isSelected ? Color.accentColor : Color.secondary)
            }
            .buttonStyle(.plain)
            .accessibilityLabel(Text(row.target.title))
            .accessibilityValue(Text(selectionDescription))

            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(row.target.title)
                        .font(.body.weight(.medium))
                    if row.target.safety == .caution {
                        Label("Review First", systemImage: "exclamationmark.circle")
                            .font(.caption.weight(.semibold))
                            .foregroundStyle(.orange)
                    }
                }
                Text(row.target.detail)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Text(row.target.locations.map(\.abbreviatedPath).joined(separator: ", "))
                    .font(.caption)
                    .foregroundStyle(.tertiary)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }

            Spacer(minLength: 12)

            sizeView
                .frame(minWidth: 80, alignment: .trailing)

            Button {
                FinderActions.reveal(row.target.locations)
            } label: {
                Image(systemName: "magnifyingglass")
            }
            .buttonStyle(.borderless)
            .help("Show in Finder")
        }
        .padding(.vertical, 4)
    }

    private var selectionDescription: String {
        row.isSelected ? "Selected" : "Not selected"
    }

    @ViewBuilder
    private var sizeView: some View {
        if !row.target.reportsSize {
            Text("Varies")
                .foregroundStyle(.secondary)
        } else if row.isMeasuring && row.size == nil {
            ProgressView()
                .controlSize(.small)
        } else {
            Text(ByteFormatting.string(row.size ?? 0))
                .monospacedDigit()
        }
    }
}

private struct CleanReportView: View {
    let report: CleanReport
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Label("Cleaning Finished", systemImage: "checkmark.circle.fill")
                .font(.title2.weight(.semibold))
            Text("Freed \(ByteFormatting.string(report.freedBytes)).")
                .font(.title3)
            if !report.failures.isEmpty {
                Text("\(report.failures.count) items couldn't be removed. They may be in use or protected by macOS.")
                    .foregroundStyle(.secondary)
                List(Array(report.failures.prefix(200))) { failure in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(failure.path)
                            .lineLimit(1)
                            .truncationMode(.middle)
                        Text(failure.reason)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
                .frame(minHeight: 160, maxHeight: 260)
            }
            Text("Scan again to update the other views.")
                .font(.callout)
                .foregroundStyle(.secondary)
            HStack {
                Spacer()
                Button("Done") {
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 540)
    }
}
