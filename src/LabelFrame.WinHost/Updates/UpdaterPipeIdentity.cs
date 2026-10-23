using System.Runtime.InteropServices;
using LabelFrame.Updates;
using Microsoft.Win32.SafeHandles;

namespace LabelFrame.WinHost.Updates;

/// <summary>将命名管道服务进程绑定到 SCM 中的客户端更新服务。</summary>
internal static class UpdaterPipeIdentity
{
    internal static void Verify(SafePipeHandle pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe, out var pipePid)) throw new InvalidDataException("无法核对更新服务进程。");
        var manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) throw new InvalidDataException("无法读取更新服务状态，请管理员检查安装。");
        try
        {
            var service = OpenService(manager, ClientUpdateContract.ServiceName, 4);
            if (service == IntPtr.Zero) throw new InvalidDataException("未安装客户端更新服务，请先由管理员手工升级一次。");
            try
            {
                if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatus>(), out _)
                    || status.CurrentState != 4 || status.ProcessId != pipePid)
                    throw new InvalidDataException("命名管道进程与系统更新服务不匹配。");
            }
            finally { _ = CloseServiceHandle(service); }
        }
        finally { _ = CloseServiceHandle(manager); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint Type, CurrentState, Controls, Win32Exit, SpecificExit, CheckPoint, WaitHint, ProcessId, Flags;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint process);
    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int level, out ServiceStatus status, int size, out int required);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr service);
}
