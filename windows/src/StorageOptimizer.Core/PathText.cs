namespace StorageOptimizer.Core;

/// <summary>
/// Path comparisons that follow Windows rules: case-insensitive, with <c>\</c> and <c>/</c>
/// treated the same. The same rules work for the POSIX paths used by tests on Linux.
/// </summary>
public static class PathText
{
    private const char Separator = '\\';

    /// <summary>Returns an absolute, normalized path suitable for comparisons.</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var full = LooksAbsolute(path) ? path : Path.GetFullPath(path);
        var unified = full.Replace('/', Separator);

        var isUnc = unified.StartsWith(@"\\", StringComparison.Ordinal);
        var drive = unified.Length >= 2 && unified[1] == ':' ? unified[..2].ToUpperInvariant() : null;
        var rest = drive is not null ? unified[2..] : unified;

        var parts = new List<string>();
        foreach (var part in rest.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
                continue;
            }
            parts.Add(part);
        }

        var joined = string.Join(Separator, parts);
        if (drive is not null)
        {
            return drive + Separator + joined;
        }
        return (isUnc ? @"\\" : @"\") + joined;
    }

    /// <summary>Whether <paramref name="path"/> equals <paramref name="root"/> or lies below it. Both must be normalized.</summary>
    public static bool IsInside(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var prefix = root.EndsWith(Separator) ? root : root + Separator;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a normalized path is a drive root (<c>C:\</c>), a UNC share root or <c>\</c>.</summary>
    public static bool IsRoot(string normalized)
    {
        if (normalized == @"\" || (normalized.Length == 3 && normalized[1] == ':' && normalized[2] == Separator))
        {
            return true;
        }
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // \\server\share is the root of a network share.
            return normalized[2..].Split(Separator, StringSplitOptions.RemoveEmptyEntries).Length <= 2;
        }
        return false;
    }

    public static bool AreEqual(string a, string b) => Normalize(a).Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The last component of a path, accepting either separator.</summary>
    public static string FileName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(['\\', '/']);
        return index >= 0 ? trimmed[(index + 1)..] : trimmed;
    }

    /// <summary>The lowercased extension without the dot, or an empty string.</summary>
    public static string Extension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 && dot < fileName.Length - 1 ? fileName[(dot + 1)..].ToLowerInvariant() : string.Empty;
    }

    /// <summary>The parent folder of a path, accepting either separator.</summary>
    public static string? Parent(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(['\\', '/']);
        if (index < 0)
        {
            return null;
        }
        if (index == 0)
        {
            return trimmed[..1];
        }
        // Keep "C:\" rather than "C:".
        return index == 2 && trimmed[1] == ':' ? trimmed[..3] : trimmed[..index];
    }

    private static bool LooksAbsolute(string path) =>
        (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        || path.StartsWith(@"\\", StringComparison.Ordinal)
        || Path.IsPathFullyQualified(path);
}
