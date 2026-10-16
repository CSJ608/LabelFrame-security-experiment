using System.Net.Sockets;

namespace LabelFrame.TransportPlugin.Tspl;

/// <summary>TSPL 通道链路（一次打开的连接）：发送指令流 / 读回状态应答；逐次建连、用后即关。public = 传输构造的注入缝（测试替身）。</summary>
public interface ITsplLink : IDisposable
{
    /// <summary>发送指令流；失败抛异常（调用方转中文）。</summary>
    void Write(byte[] buffer, int timeoutMs);

    /// <summary>读回应答（最多 maxBytes，超时毫秒）；超时 / 无应答返回 null。</summary>
    byte[]? Read(int maxBytes, int timeoutMs);
}

/// <summary>TCP 9100 链路。</summary>
internal sealed class TcpTsplLink : ITsplLink
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    public TcpTsplLink(string host, int port)
    {
        _client = new TcpClient();
        try
        {
            _client.Connect(host, port);
            _stream = _client.GetStream();
        }
        catch
        {
            _client.Dispose();
            throw;
        }
    }

    public void Write(byte[] buffer, int timeoutMs)
    {
        _stream.WriteTimeout = timeoutMs;
        _stream.Write(buffer, 0, buffer.Length);
        _stream.Flush();
    }

    public byte[]? Read(int maxBytes, int timeoutMs)
    {
        _stream.ReadTimeout = timeoutMs;
        var buffer = new byte[maxBytes];
        try
        {
            var read = _stream.Read(buffer, 0, maxBytes);
            return read == 0 ? null : buffer[..read];
        }
        catch (IOException)
        {
            // ReadTimeout 到点抛 IOException：按无应答语义返回 null
            return null;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
    }
}

/// <summary>USB 免驱动直连连链路（usbprint 设备路径）。</summary>
internal sealed class UsbTsplLink : ITsplLink
{
    private readonly UsbRawDevice _device;

    public UsbTsplLink(string devicePath)
    {
        _device = new UsbRawDevice(devicePath);
    }

    public void Write(byte[] buffer, int timeoutMs) => _device.Write(buffer, timeoutMs);

    public byte[]? Read(int maxBytes, int timeoutMs) => _device.Read(maxBytes, timeoutMs);

    public void Dispose() => _device.Dispose();
}
