namespace StorageOptimizer.Core;

/// <summary>
/// High-level kinds of files used to group disk usage. The order is fixed: it also defines the
/// order of colors and of the segments in the usage bar.
/// </summary>
public enum FileCategory
{
    Document,
    Application,
    Image,
    Audio,
    Video,
    Archive,
    Developer,
    DiskImage,
    Other,
}

public static class FileCategories
{
    public static IReadOnlyList<FileCategory> All { get; } = Enum.GetValues<FileCategory>();

    public static string DisplayName(this FileCategory category) => category switch
    {
        FileCategory.Document => "Documents",
        FileCategory.Application => "Applications",
        FileCategory.Image => "Images",
        FileCategory.Audio => "Audio",
        FileCategory.Video => "Videos",
        FileCategory.Archive => "Archives",
        FileCategory.Developer => "Developer",
        FileCategory.DiskImage => "Disk Images",
        _ => "Other",
    };

    /// <summary>Categorizes a file by its extension (with or without the dot, any case).</summary>
    public static FileCategory ForExtension(string extension)
    {
        var key = extension.TrimStart('.').ToLowerInvariant();
        return key.Length > 0 && Map.TryGetValue(key, out var category) ? category : FileCategory.Other;
    }

    private static readonly Dictionary<string, FileCategory> Map = BuildMap();

    private static Dictionary<string, FileCategory> BuildMap()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.Ordinal);
        void Register(FileCategory category, string extensions)
        {
            foreach (var ext in extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                map[ext] = category;
            }
        }

        Register(FileCategory.Video, "mp4 m4v mov avi mkv wmv flv webm mpg mpeg 3gp mts m2ts vob braw r3d");
        Register(FileCategory.Image, "jpg jpeg png gif heic heif tif tiff bmp webp avif raw cr2 cr3 nef arw dng raf orf rw2 psd svg ico jxr exr hdr tga");
        Register(FileCategory.Audio, "mp3 m4a m4b aac wav aif aiff flac ogg opus wma caf mid midi");
        Register(FileCategory.Document, "pdf doc docx xls xlsx ppt pptx txt rtf md csv epub odt ods odp one xps oxps vsdx pub pst ost");
        Register(FileCategory.Archive, "zip rar 7z tar gz tgz bz2 tbz xz txz zst lz4 lzma cab");
        Register(FileCategory.DiskImage, "iso img vhd vhdx vmdk vdi qcow2 wim esd dmg avhdx");
        Register(FileCategory.Application, "exe msi msix msixbundle appx appxbundle apk aab ipa");
        Register(FileCategory.Developer,
            "c cc cpp cxx h hpp cs fs vb java kt kts swift m mm go rs py pyc rb php dart lua js mjs cjs ts tsx jsx " +
            "html css scss json yaml yml xml toml gradle vue svelte sql sh ps1 psm1 bat cmd " +
            "dll pdb lib obj o a so dylib class jar war aar wasm pch ipch pcm map node nupkg snupkg unitypackage");
        return map;
    }
}
