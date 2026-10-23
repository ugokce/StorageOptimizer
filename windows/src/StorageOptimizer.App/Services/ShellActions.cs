using System.Diagnostics;
using System.IO;
using System.Windows;
using StorageOptimizer.Core;

namespace StorageOptimizer.App.Services;

/// <summary>File Explorer, default app and clipboard helpers.</summary>
public static class ShellActions
{
    /// <summary>Opens File Explorer with the items selected (one window per folder).</summary>
    public static void ShowInExplorer(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }
        if (paths.Count == 1)
        {
            // A drive root can't be selected inside a parent folder, so open it instead.
            var arguments = PathText.IsRoot(PathText.Normalize(paths[0])) ? $"\"{paths[0]}\"" : $"/select,\"{paths[0]}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
            return;
        }

        foreach (var group in paths.GroupBy(path => PathText.Parent(path) ?? path, StringComparer.OrdinalIgnoreCase))
        {
            var folder = NativeMethods.ILCreateFromPath(group.Key);
            var items = group.Select(NativeMethods.ILCreateFromPath).Where(pidl => pidl != IntPtr.Zero).ToArray();
            try
            {
                if (folder != IntPtr.Zero && items.Length > 0)
                {
                    _ = NativeMethods.SHOpenFolderAndSelectItems(folder, (uint)items.Length, items, 0);
                }
            }
            finally
            {
                foreach (var item in items)
                {
                    NativeMethods.ILFree(item);
                }
                if (folder != IntPtr.Zero)
                {
                    NativeMethods.ILFree(folder);
                }
            }
        }
    }

    public static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowInExplorer([path]);
        }
    }

    public static void CopyPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another app is holding the clipboard; copying is best effort.
        }
    }

    /// <summary>Whether any process with one of these names (without .exe) is running.</summary>
    public static IReadOnlyList<string> RunningProcesses(IEnumerable<string> names)
    {
        var running = new List<string>();
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length > 0)
            {
                running.Add(name);
            }
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
        return running;
    }
}
