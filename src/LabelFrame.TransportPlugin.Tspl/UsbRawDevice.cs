using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>
/// USB 打印机原始设备通道（usbprint 类驱动，CreateFile 直开绕打印队列——2026-10-16 TSC TT065-60
/// 实测双向通）。同步 IO + 看门狗超时：实测该接口重叠 IO 挂起不收尾（结构对齐后仍超时），同步读写即时完成；
/// 超时后放弃等待交调用方按失败语义处理，被看门狗放弃的阻塞线程随链路关闭（句柄释放中止未决 IO）退出。
/// </summary>
internal sealed class UsbRawDevice : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    private readonly SafeFileHandle _device;

    /// <summary>打开设备路径（来自 UsbPrinterDeviceDiscovery）；打开失败抛中文 InvalidOperationException。</summary>
    public UsbRawDevice(string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        _device = CreateFile(devicePath, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (_device.IsInvalid)
        {
            throw new InvalidOperationException($"打开 USB 打印机失败（{devicePath}）：设备不存在、被占用或无访问权限。");
        }
    }

    /// <summary>发送指令流（超时毫秒）；失败 / 超时抛 InvalidOperationException。Win32 错误号在执行线程上捕获。</summary>
    public void Write(byte[] buffer, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var task = Task.Run(() =>
        {
            if (WriteFile(_device, buffer, (uint)buffer.Length, out var written, IntPtr.Zero) && written == buffer.Length)
            {
                return 0;
            }

            return Marshal.GetLastWin32Error();
        });
        if (!task.Wait(timeoutMs))
        {
            throw new InvalidOperationException($"写入 USB 打印机超时（{buffer.Length} 字节，{timeoutMs} 毫秒）。");
        }

        if (task.Result != 0)
        {
            throw new InvalidOperationException($"写入 USB 打印机失败（Win32 错误 {task.Result}）。");
        }
    }

    /// <summary>读回应答（最多 maxBytes，超时毫秒）；超时 / 无应答 / 读失败返回 null。</summary>
    public byte[]? Read(int maxBytes, int timeoutMs)
    {
        var task = Task.Run(() =>
        {
            var buffer = new byte[maxBytes];
            return ReadFile(_device, buffer, (uint)maxBytes, out var read, IntPtr.Zero) && read > 0
                ? (Error: 0, Data: buffer[..(int)read])
                : (Error: Marshal.GetLastWin32Error(), Data: null);
        });
        return task.Wait(timeoutMs) ? task.Result.Data : null;
    }

    public void Dispose() => _device.Dispose();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile([MarshalAs(UnmanagedType.LPWStr)] string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
}
