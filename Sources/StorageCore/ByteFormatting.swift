import Foundation

public enum ByteFormatting {
    /// Formats a byte count the way Finder does (e.g. "1.2 GB").
    public static func string(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}
