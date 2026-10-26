using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LabelFrame.Core.Jobs;

/// <summary>查询作业库与同目录备份实际所在文件系统的可用空间。</summary>
internal static class JobStorageSpace
{
    internal static long AvailableForDatabase(string databasePath, Func<string, long>? query = null)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var target = File.ResolveLinkTarget(fullPath, returnFinalTarget: true)?.FullName ?? fullPath;
        query ??= AvailableForDirectory;
        var dataSpace = query(Path.GetDirectoryName(target)!);
        var backupSpace = query(Path.GetDirectoryName(fullPath)!);
        return Math.Min(dataSpace, backupSpace);
    }

    internal static long AvailableForDirectory(string directory)
    {
        if (!OperatingSystem.IsWindows())
            return new DriveInfo(directory).AvailableFreeSpace;

        // Windows DriveInfo 会截成卷根，目录挂载和链接必须交给原生路径查询。
        if (!GetDiskFreeSpaceEx(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
                out var available, out _, out _))
            throw new IOException($"无法查询作业库目录可用空间：{directory}",
                new Win32Exception(Marshal.GetLastWin32Error()));
        return checked((long)available);
    }

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);
}
