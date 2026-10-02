import Foundation

/// Capacity of the startup disk.
public struct VolumeInfo: Equatable, Sendable {
    public let name: String
    public let totalCapacity: Int64
    /// Space available for important data, including purgeable space macOS can free on demand.
    public let availableCapacity: Int64

    public init(name: String, totalCapacity: Int64, availableCapacity: Int64) {
        self.name = name
        self.totalCapacity = totalCapacity
        self.availableCapacity = availableCapacity
    }

    public var usedCapacity: Int64 { max(0, totalCapacity - availableCapacity) }

    public var usedFraction: Double {
        totalCapacity > 0 ? Double(usedCapacity) / Double(totalCapacity) : 0
    }

    /// Reads the capacity of the volume containing `url` (the home folder by default).
    public static func current(for url: URL = FileManager.default.homeDirectoryForCurrentUser) -> VolumeInfo? {
        let capacityKeys: Set<URLResourceKey> = [
            .volumeTotalCapacityKey,
            .volumeAvailableCapacityForImportantUsageKey,
            .volumeAvailableCapacityKey,
        ]
        guard let values = try? url.resourceValues(forKeys: capacityKeys),
              let total = values.volumeTotalCapacity else { return nil }
        let available = values.volumeAvailableCapacityForImportantUsage ?? Int64(values.volumeAvailableCapacity ?? 0)

        // The startup volume's name ("Macintosh HD") reads better than the data volume's.
        let nameKeys: Set<URLResourceKey> = [.volumeLocalizedNameKey, .volumeNameKey]
        let nameValues = try? URL(fileURLWithPath: "/").resourceValues(forKeys: nameKeys)
        let name = nameValues?.volumeLocalizedName ?? nameValues?.volumeName ?? "Startup Disk"

        return VolumeInfo(name: name, totalCapacity: Int64(total), availableCapacity: available)
    }
}
