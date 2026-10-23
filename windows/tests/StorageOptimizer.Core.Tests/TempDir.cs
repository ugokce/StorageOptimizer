namespace StorageOptimizer.Core.Tests;

/// <summary>A temporary folder for file system fixtures, deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "StorageOptimizerTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string this[string relativePath] => System.IO.Path.Join(Path, relativePath);

    /// <summary>Creates a file of <paramref name="size"/> bytes, creating intermediate folders.</summary>
    public string MakeFile(string relativePath, int size)
    {
        var file = this[relativePath];
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, new byte[size]);
        return file;
    }

    public string MakeDirectory(string relativePath)
    {
        var directory = this[relativePath];
        Directory.CreateDirectory(directory);
        return directory;
    }

    public bool Exists(string relativePath) => File.Exists(this[relativePath]) || Directory.Exists(this[relativePath]);

    /// <summary>Creates a directory symbolic link, or returns false where that needs privileges the test lacks.</summary>
    public bool TryMakeDirectoryLink(string relativePath, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(this[relativePath], target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort; the OS cleans the temp folder eventually.
        }
    }
}
