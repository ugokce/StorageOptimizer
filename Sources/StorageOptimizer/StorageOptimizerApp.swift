import AppKit
import SwiftUI

@main
struct StorageOptimizerApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @State private var appState = AppState()

    var body: some Scene {
        WindowGroup("Storage Optimizer") {
            ContentView()
                .environment(appState)
                .frame(minWidth: 820, minHeight: 520)
        }
        .defaultSize(width: 1240, height: 820)
        .windowResizability(.contentMinSize)
        .commands {
            CommandGroup(after: .newItem) {
                Button("Scan Again") {
                    appState.startScan()
                }
                .keyboardShortcut("r", modifiers: .command)
                .disabled(appState.isScanning)

                Button("Stop Scan") {
                    appState.cancelScan()
                }
                .keyboardShortcut(".", modifiers: .command)
                .disabled(!appState.isScanning)
            }
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        // Needed when launched with `swift run`, where there is no app bundle telling macOS
        // that this is a regular app with a Dock icon and windows.
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }
}
