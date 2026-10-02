import AppKit
import Combine
import StorageCore
import SwiftUI

@MainActor
struct ContentView: View {
    @Environment(AppState.self) private var appState

    var body: some View {
        @Bindable var appState = appState

        NavigationSplitView {
            List(selection: $appState.selection) {
                Section("Storage") {
                    ForEach(SidebarItem.allCases) { item in
                        Label(item.title, systemImage: item.systemImage)
                            .tag(item)
                    }
                }
            }
            .navigationSplitViewColumnWidth(min: 190, ideal: 210)
        } detail: {
            VStack(spacing: 0) {
                if !appState.hasFullDiskAccess {
                    FullDiskAccessBanner()
                }
                if appState.isScanning {
                    ScanStatusBar()
                }
                detailView
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .toolbar {
            ToolbarItem(placement: .navigation) {
                scanTargetMenu
            }
            ToolbarItem(placement: .primaryAction) {
                scanButton
            }
        }
        .alert(appState.alertTitle, isPresented: $appState.isAlertPresented) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(appState.alertMessage)
        }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            appState.refreshEnvironment()
        }
        .task {
            appState.cacheModel.refresh()
        }
    }

    @ViewBuilder
    private var detailView: some View {
        switch appState.selection ?? .overview {
        case .overview:
            OverviewView()
        case .largeFiles:
            LargeFilesView()
        case .folders:
            FolderBrowserView()
        case .fileTypes:
            FileTypesView()
        case .cacheCleaner:
            CacheCleanerView()
        }
    }

    private var scanTargetMenu: some View {
        Menu {
            Button {
                appState.scanTarget = .home
            } label: {
                Label("Home Folder", systemImage: "house")
            }
            Button {
                appState.scanTarget = .entireDisk
            } label: {
                Label("Entire Disk", systemImage: "internaldrive")
            }
            Divider()
            Button("Choose Folder…") {
                appState.chooseFolderAndScan()
            }
        } label: {
            Label(appState.scanTarget.title, systemImage: appState.scanTarget.systemImage)
                .labelStyle(.titleAndIcon)
        }
        .disabled(appState.isScanning)
        .help("Choose what to scan")
    }

    @ViewBuilder
    private var scanButton: some View {
        if appState.isScanning {
            Button {
                appState.cancelScan()
            } label: {
                Label("Stop", systemImage: "stop.circle")
            }
            .help("Stop the current scan")
        } else {
            Button {
                appState.startScan()
            } label: {
                Label(appState.result == nil ? "Scan" : "Scan Again", systemImage: "arrow.clockwise")
                    .labelStyle(.titleAndIcon)
            }
            .help("Scan \(appState.scanTarget.title)")
        }
    }
}
