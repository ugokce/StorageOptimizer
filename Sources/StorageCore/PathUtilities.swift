import Foundation

/// Helpers for comparing file system paths consistently.
public enum PathUtilities {
    /// A normalized absolute path used for comparisons.
    ///
    /// Standardizes `.`/`..`, drops trailing slashes and maps `/private/var`, `/private/tmp`
    /// and `/private/etc` to their well-known `/var`, `/tmp` and `/etc` aliases so that paths
    /// coming from different Foundation APIs compare equal.
    public static func canonicalPath(_ url: URL) -> String {
        canonicalPath(url.standardizedFileURL.path)
    }

    public static func canonicalPath(_ rawPath: String) -> String {
        var path = rawPath.isEmpty ? "/" : rawPath
        while path.count > 1 && path.hasSuffix("/") {
            path.removeLast()
        }
        for alias in ["/private/var", "/private/tmp", "/private/etc"] {
            if path == alias || path.hasPrefix(alias + "/") {
                path.removeFirst("/private".count)
                break
            }
        }
        return path
    }

    /// Whether `path` equals `root` or lives somewhere below it. Both must be canonical.
    public static func isPath(_ path: String, inside root: String) -> Bool {
        if root == "/" { return path.hasPrefix("/") }
        return path == root || path.hasPrefix(root + "/")
    }
}

public extension URL {
    /// The path with the home folder abbreviated to `~`.
    var abbreviatedPath: String {
        (path as NSString).abbreviatingWithTildeInPath
    }
}
