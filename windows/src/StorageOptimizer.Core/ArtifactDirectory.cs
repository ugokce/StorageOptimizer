namespace StorageOptimizer.Core;

/// <summary>Kinds of project folders that build tools can recreate.</summary>
public enum ArtifactKind
{
    NodeModules,
    UnityLibrary,
    DotNetBuild,
    CargoTarget,
    Gradle,
    PythonVirtualEnv,
    NextBuild,
}

public static class ArtifactKinds
{
    public static string DisplayName(this ArtifactKind kind) => kind switch
    {
        ArtifactKind.NodeModules => "node_modules",
        ArtifactKind.UnityLibrary => "Unity Library",
        ArtifactKind.DotNetBuild => ".NET bin/obj",
        ArtifactKind.CargoTarget => "Cargo target",
        ArtifactKind.Gradle => "Gradle .gradle",
        ArtifactKind.PythonVirtualEnv => "Python virtualenv",
        _ => "Next.js .next",
    };

    /// <summary>How the folder comes back after deleting it.</summary>
    public static string RestoreHint(this ArtifactKind kind) => kind switch
    {
        ArtifactKind.NodeModules => "Restored by running npm, yarn or pnpm install in the project.",
        ArtifactKind.UnityLibrary => "Unity rebuilds it the next time the project opens; large projects take a while to reimport.",
        ArtifactKind.DotNetBuild => "Rebuilt by the next build in Visual Studio or dotnet build.",
        ArtifactKind.CargoTarget => "Rebuilt by the next cargo build.",
        ArtifactKind.Gradle => "Rebuilt by the next Gradle build.",
        ArtifactKind.PythonVirtualEnv => "Recreate it with python -m venv and reinstall the requirements.",
        _ => "Rebuilt by the next Next.js build or dev server start.",
    };

    private static readonly string[] DotNetProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    /// <summary>Detects whether a folder named <paramref name="name"/> is a rebuildable project folder.</summary>
    internal static ArtifactKind? Detect(string directory, string name)
    {
        var parent = PathText.Parent(directory);
        if (parent is null)
        {
            return null;
        }

        bool Sibling(params string[] names) => names.Any(n => Exists(Path.Join(parent, n)));

        switch (name.ToLowerInvariant())
        {
            case "node_modules":
                // Global installs (e.g. %APPDATA%\npm\node_modules) have no package.json beside them.
                return Sibling("package.json") ? ArtifactKind.NodeModules : null;
            case "library":
                return Directory.Exists(Path.Join(parent, "Assets")) && Directory.Exists(Path.Join(parent, "ProjectSettings"))
                    ? ArtifactKind.UnityLibrary : null;
            case "bin":
            case "obj":
                return HasDotNetProject(parent) ? ArtifactKind.DotNetBuild : null;
            case "target":
                return Sibling("Cargo.toml") ? ArtifactKind.CargoTarget : null;
            case ".gradle":
                return Sibling("build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts") ? ArtifactKind.Gradle : null;
            case "venv":
            case ".venv":
            case "env":
                return File.Exists(Path.Join(directory, "pyvenv.cfg")) ? ArtifactKind.PythonVirtualEnv : null;
            case ".next":
                return Sibling("package.json") ? ArtifactKind.NextBuild : null;
            default:
                return null;
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool HasDotNetProject(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Any(file => DotNetProjectExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>A rebuildable project folder found during a scan.</summary>
public sealed record ArtifactDirectory(string Path, ArtifactKind Kind, long Size)
{
    /// <summary>Name of the project folder that contains the artifact.</summary>
    public string ProjectName => PathText.FileName(PathText.Parent(Path) ?? Path);
}
