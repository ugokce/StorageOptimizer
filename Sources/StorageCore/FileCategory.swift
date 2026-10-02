import Foundation
import UniformTypeIdentifiers

/// High-level kinds of files used to group disk usage.
///
/// The case order is fixed and also defines the order of colors and of stacked usage bars.
public enum FileCategory: String, CaseIterable, Identifiable, Hashable, Sendable {
    case document
    case application
    case image
    case audio
    case video
    case archive
    case developer
    case diskImage
    case other

    public var id: String { rawValue }

    public var displayName: String {
        switch self {
        case .document: "Documents"
        case .application: "Applications"
        case .image: "Images"
        case .audio: "Audio"
        case .video: "Videos"
        case .archive: "Archives"
        case .developer: "Developer"
        case .diskImage: "Disk Images"
        case .other: "Other"
        }
    }

    /// SF Symbol name representing the category.
    public var systemImage: String {
        switch self {
        case .document: "doc.text"
        case .application: "app.badge"
        case .image: "photo"
        case .audio: "music.note"
        case .video: "film"
        case .archive: "archivebox"
        case .developer: "hammer"
        case .diskImage: "opticaldiscdrive"
        case .other: "doc"
        }
    }

    /// Categorizes a file (or package) by its extension. The extension is matched case-insensitively.
    public static func category(forExtension fileExtension: String) -> FileCategory {
        let ext = fileExtension.lowercased()
        guard !ext.isEmpty else { return .other }
        if let known = extensionMap[ext] {
            return known
        }
        guard let type = UTType(filenameExtension: ext) else { return .other }
        return category(for: type)
    }

    static func category(for type: UTType) -> FileCategory {
        if type.conforms(to: .applicationBundle) || type.conforms(to: .application) { return .application }
        if type.conforms(to: .diskImage) { return .diskImage }
        if type.conforms(to: .archive) { return .archive }
        if type.conforms(to: .movie) || type.conforms(to: .video) { return .video }
        if type.conforms(to: .audio) { return .audio }
        if type.conforms(to: .image) { return .image }
        if type.conforms(to: .sourceCode) { return .developer }
        if type.conforms(to: .pdf) || type.conforms(to: .presentation) || type.conforms(to: .spreadsheet)
            || type.conforms(to: .text) {
            return .document
        }
        return .other
    }

    private static let extensionMap: [String: FileCategory] = {
        var map: [String: FileCategory] = [:]
        func register(_ category: FileCategory, _ extensions: [String]) {
            for ext in extensions { map[ext] = category }
        }
        register(.video, [
            "mp4", "m4v", "mov", "avi", "mkv", "wmv", "flv", "webm", "mpg", "mpeg", "3gp", "mts", "m2ts",
            "vob", "braw", "r3d", "fcpbundle", "imovielibrary", "tvlibrary",
        ])
        register(.image, [
            "jpg", "jpeg", "png", "gif", "heic", "heif", "tif", "tiff", "bmp", "webp", "avif", "raw", "cr2",
            "cr3", "nef", "arw", "dng", "raf", "orf", "rw2", "psd", "svg", "ico", "icns", "sketch", "fig",
            "photoslibrary", "aplibrary", "photolibrary",
        ])
        register(.audio, [
            "mp3", "m4a", "m4b", "m4p", "aac", "wav", "aif", "aiff", "flac", "ogg", "opus", "wma", "caf",
            "mid", "midi", "logicx", "band", "musiclibrary",
        ])
        register(.document, [
            "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "pages", "numbers", "key", "txt", "rtf",
            "rtfd", "md", "csv", "epub", "odt", "ods", "odp", "tex",
        ])
        register(.archive, [
            "zip", "rar", "7z", "tar", "gz", "tgz", "bz2", "tbz", "xz", "txz", "zst", "lz4", "lzma", "cpgz",
            "sit", "sitx", "xip",
        ])
        register(.diskImage, [
            "dmg", "iso", "img", "sparseimage", "sparsebundle", "toast", "vmdk", "vdi", "vhd", "vhdx",
            "qcow2", "ipsw", "utm", "pvm",
        ])
        register(.application, ["app", "pkg", "mpkg", "ipa", "apk", "aab", "exe", "msi", "prefpane"])
        register(.developer, [
            "swift", "m", "mm", "h", "hpp", "c", "cc", "cpp", "cs", "java", "kt", "kts", "js", "mjs", "cjs",
            "ts", "tsx", "jsx", "py", "pyc", "rb", "go", "rs", "php", "dart", "lua", "sh", "zsh", "sql",
            "html", "css", "scss", "json", "yaml", "yml", "xml", "toml", "gradle", "vue", "svelte",
            "o", "a", "dylib", "so", "class", "jar", "war", "aar", "wasm", "pch", "pcm", "swiftmodule",
            "swiftinterface", "swiftdoc", "dsym", "xcarchive", "framework", "xcframework", "bundle",
            "xcodeproj", "xcworkspace", "playground", "map", "node",
        ])
        return map
    }()
}

/// Memoizes extension lookups during a scan; `UTType` lookups are comparatively expensive.
struct CategoryResolver {
    private var cache: [String: FileCategory] = [:]

    mutating func category(forExtension fileExtension: String) -> FileCategory {
        if let cached = cache[fileExtension] {
            return cached
        }
        let category = FileCategory.category(forExtension: fileExtension)
        cache[fileExtension] = category
        return category
    }
}
