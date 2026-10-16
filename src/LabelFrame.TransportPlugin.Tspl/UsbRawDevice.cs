using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>
/// USB 打印机原始设备通道（usbprint 类驱动，CreateFile 直开绕打印队列——2026-10-16 TSC TT065-60
/// 实测双向通）。重叠 IO + 每操作超时：OVERLAPPED 结构驻留非托管内存直至操作收尾（值封送只在调用期
/// 有效，未决 IO 期间结构必须存活），超时取消 IO 并按无应答处理（实测语义：任务后短暂无应答不挂死调用方）。
/// </summary>
internal sealed class UsbRawDevice : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorIoPending = 997;

    private readonly SafeFileHandle _device;
    private readonly SafeFileHandle _eventHandle;

    /// <summary>打开设备路径（来自 UsbPrinterDeviceDiscovery）；打开失败抛中文 InvalidOperationException。</summary>
    public UsbRawDevice(string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        _eventHandle = CreateEvent(IntPtr.Zero, true, false, null)
            ?? throw new InvalidOperationException("打开 USB 打印机 IO 事件对象失败。");
        _device = CreateFile(devicePath, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (_device.IsInvalid)
        {
            _eventHandle.Dispose();
            throw new InvalidOperationException($"打开 USB 打印机失败（{devicePath}）：设备不存在、被占用或无访问权限。");
        }
    }

    /// <summary>发送指令流（超时毫秒）；失败 / 超时抛 InvalidOperationException。</summary>
    public void Write(byte[] buffer, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var overlapped = NewOverlapped();
        try
        {
            if (!WriteFile(_device, buffer, (uint)buffer.Length, out _, overlapped))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorIoPending)
                {
                    throw new InvalidOperationException($"写入 USB 打印机失败（Win32 错误 {error}）。");
                }

                if (!WaitCompletion(overlapped, timeoutMs, out _))
                {
                    CancelIo();
                    throw new InvalidOperationException($"写入 USB 打印机超时（{buffer.Length} 字节，{timeoutMs} 毫秒）。");
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(overlapped);
        }
    }

    /// <summary>读回应答（最多 maxBytes，超时毫秒）；超时 / 无应答返回 null。</summary>
    public byte[]? Read(int maxBytes, int timeoutMs)
    {
        var buffer = new byte[maxBytes];
        var overlapped = NewOverlapped();
        try
        {
            if (!ReadFile(_device, buffer, (uint)maxBytes, out var read, overlapped))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorIoPending)
                {
                    return null;
                }

                if (!WaitCompletion(overlapped, timeoutMs, out read))
                {
                    CancelIo();
                    return null;
                }
            }

            return read == 0 ? null : buffer[..(int)read];
        }
        finally
        {
            Marshal.FreeHGlobal(overlapped);
        }
    }

    /// <summary>构造 OVERLAPPED（驻留 HGlobal，调用方负责释放）并复位事件。</summary>
    private IntPtr NewOverlapped()
    {
        if (!ResetEvent(_eventHandle))
        {
            throw new InvalidOperationException("重置 USB 设备 IO 事件失败。");
        }

        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());
        Marshal.StructureToPtr(new Overlapped { EventHandle = _eventHandle.DangerousGetHandle() }, pointer, false);
        return pointer;
    }

    /// <summary>等待重叠 IO 完成（超时毫秒）；完成取回传输字节数，超时返回 false。</summary>
    private bool WaitCompletion(IntPtr overlapped, int timeoutMs, out uint transferred)
    {
        if (WaitForSingleObject(_eventHandle, (uint)timeoutMs) != 0)
        {
            transferred = 0;
            return false;
        }

        return GetOverlappedResult(_device, overlapped, out transferred, false);
    }

    private void CancelIo()
    {
        try
        {
            CancelIoEx(_device, IntPtr.Zero);
        }
        catch
        {
            // 取消失败不放大：调用方按超时语义处理
        }
    }

    public void Dispose()
    {
        _device.Dispose();
        _eventHandle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Overlapped
    {
        public nuint OffsetLow;
        public nuint OffsetHigh;
        public IntPtr EventHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile([MarshalAs(UnmanagedType.LPWStr)] string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle hFile, IntPtr lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle? CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, [MarshalAs(UnmanagedType.LPWStr)] string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ResetEvent(SafeFileHandle hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeFileHandle hHandle, uint dwMilliseconds);
}
