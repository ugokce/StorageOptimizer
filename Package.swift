// swift-tools-version:5.10
import PackageDescription

let package = Package(
    name: "StorageOptimizer",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "StorageOptimizer", targets: ["StorageOptimizer"]),
        .library(name: "StorageCore", targets: ["StorageCore"]),
    ],
    targets: [
        .target(name: "StorageCore"),
        .executableTarget(
            name: "StorageOptimizer",
            dependencies: ["StorageCore"]
        ),
        .testTarget(
            name: "StorageCoreTests",
            dependencies: ["StorageCore"]
        ),
    ]
)
