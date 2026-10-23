using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using StorageOptimizer.Core;

namespace StorageOptimizer.App.Services;

/// <summary>The Windows Recycle Bin: moving items into it, measuring it and emptying it.</summary>
public sealed class RecycleBin : IRecycleBin
{
    public long QuerySize()
    {
        var info = new NativeMethods.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
        var result = NativeMethods.SHQueryRecycleBin(null, ref info);
        return result == 0 ? info.i64Size : 0;
    }

    public void Empty()
    {
        var result = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null,
            NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND);
        // E_UNEXPECTED is returned when the Recycle Bin is already empty.
        if (result != 0 && result != unchecked((int)0x8000FFFF))
        {
            throw new IOException($"Windows couldn't empty the Recycle Bin (error 0x{result:X8}).");
        }
    }

    /// <summary>Moves a file or folder to the Recycle Bin. Windows warns first if it's too big to be recycled.</summary>
    public static void Send(string path)
    {
        var operation = new NativeMethods.SHFILEOPSTRUCT
        {
            wFunc = NativeMethods.FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = (ushort)(NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION | NativeMethods.FOF_NOERRORUI
                | NativeMethods.FOF_SILENT | NativeMethods.FOF_WANTNUKEWARNING),
        };
        var result = NativeMethods.SHFileOperation(ref operation);
        if (result != 0)
        {
            throw new IOException($"Windows couldn't move it to the Recycle Bin (error 0x{result:X}). It may be open in another app.");
        }
        if (operation.fAnyOperationsAborted)
        {
            throw new OperationCanceledException("Moving to the Recycle Bin was cancelled.");
        }
    }
}

/// <summary>Local drives and their properties.</summary>
public static class Drives
{
    public static string SystemDriveRoot => Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    /// <summary>Ready fixed and removable drives.</summary>
    public static IReadOnlyList<DriveInfo> LocalDrives() =>
        DriveInfo.GetDrives()
            .Where(drive => drive.DriveType is DriveType.Fixed or DriveType.Removable && IsReady(drive))
            .ToList();

    public static string DisplayName(DriveInfo drive)
    {
        var letter = drive.Name.TrimEnd('\\');
        string label;
        try
        {
            label = drive.VolumeLabel;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            label = string.Empty;
        }
        if (string.IsNullOrWhiteSpace(label))
        {
            label = drive.DriveType == DriveType.Removable ? "USB Drive" : "Local Disk";
        }
        return $"{label} ({letter})";
    }

    public static long ClusterSize(string root)
    {
        return NativeMethods.GetDiskFreeSpace(root, out var sectorsPerCluster, out var bytesPerSector, out _, out _)
            ? (long)sectorsPerCluster * bytesPerSector
            : FileSizing.DefaultClusterSize;
    }

    public static bool IsNetworkPath(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }
        try
        {
            return new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool IsReady(DriveInfo drive)
    {
        try
        {
            return drive.IsReady;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

public static class Elevation
{
    public static bool IsElevated => Environment.IsPrivilegedProcess;

    /// <summary>Restarts the app as administrator. Returns false if the user declined the prompt.</summary>
    public static bool RestartElevated()
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}

/// <summary>Questions and messages shown to the user (replaced by a silent version in the smoke test).</summary>
public interface IDialogs
{
    bool Confirm(string title, string message);
    void Inform(string title, string message, bool isWarning = false);
    string? PickFolder();
}

public sealed class WindowsDialogs : IDialogs
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public void Inform(string title, string message, bool isWarning = false) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK,
            isWarning ? MessageBoxImage.Warning : MessageBoxImage.Information);

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to scan" };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }
}
