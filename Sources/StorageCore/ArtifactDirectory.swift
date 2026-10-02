import Foundation

/// Kinds of project folders that build tools can recreate (e.g. `node_modules`).
public enum ArtifactKind: String, CaseIterable, Hashable, Sendable {
    case nodeModules
    case cocoaPods
    case swiftBuild
    case xcodeDerivedData
    case gradle
    case cargoTarget
    case pythonVirtualEnv
    case nextBuild

    public var displayName: String {
        switch self {
        case .nodeModules: "node_modules"
        case .cocoaPods: "CocoaPods Pods"
        case .swiftBuild: "SwiftPM .build"
        case .xcodeDerivedData: "DerivedData"
        case .gradle: "Gradle .gradle"
        case .cargoTarget: "Cargo target"
        case .pythonVirtualEnv: "Python virtualenv"
        case .nextBuild: "Next.js .next"
        }
    }

    /// How the folder is restored after deleting it.
    public var restoreHint: String {
        switch self {
        case .nodeModules: "Restored by running npm, yarn or pnpm install in the project."
        case .cocoaPods: "Restored by running pod install in the project."
        case .swiftBuild: "Rebuilt by the next swift build."
        case .xcodeDerivedData: "Rebuilt by the next Xcode build."
        case .gradle: "Rebuilt by the next Gradle build."
        case .cargoTarget: "Rebuilt by the next cargo build."
        case .pythonVirtualEnv: "Recreate it with python -m venv and reinstall the requirements."
        case .nextBuild: "Rebuilt by the next Next.js build or dev server start."
        }
    }

    /// Detects whether `directory` (named `name`) is a rebuildable project folder.
    static func detect(directory: URL, name: String) -> ArtifactKind? {
        let fileManager = FileManager.default
        let parent = directory.deletingLastPathComponent()
        func siblingExists(_ names: String...) -> Bool {
            names.contains { fileManager.fileExists(atPath: parent.appendingPathComponent($0).path) }
        }
        switch name {
        case "node_modules":
            // Global installs (e.g. /usr/local/lib/node_modules) have no package.json beside them.
            return siblingExists("package.json") ? .nodeModules : nil
        case "Pods":
            return siblingExists("Podfile") ? .cocoaPods : nil
        case ".build":
            return siblingExists("Package.swift") ? .swiftBuild : nil
        case "DerivedData":
            return .xcodeDerivedData
        case ".gradle":
            return siblingExists("build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts")
                ? .gradle : nil
        case "target":
            return siblingExists("Cargo.toml") ? .cargoTarget : nil
        case "venv", ".venv", "env":
            return fileManager.fileExists(atPath: directory.appendingPathComponent("pyvenv.cfg").path)
                ? .pythonVirtualEnv : nil
        case ".next":
            return siblingExists("package.json") ? .nextBuild : nil
        default:
            return nil
        }
    }
}

/// A rebuildable project folder found during a scan.
public struct ArtifactDirectory: Identifiable, Hashable, Sendable {
    public let url: URL
    public let kind: ArtifactKind
    public let size: Int64

    public init(url: URL, kind: ArtifactKind, size: Int64) {
        self.url = url
        self.kind = kind
        self.size = size
    }

    public var id: URL { url }
    /// Name of the project folder that contains the artifact.
    public var projectName: String { url.deletingLastPathComponent().lastPathComponent }
}
