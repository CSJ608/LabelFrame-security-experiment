using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LabelFrame.Server;

/// <summary>
/// 本机 IPv4 候选枚举（· ：下载中心「连接信息」卡地址候选来源
/// 参照 WinHost <see cref="LabelFrame.WinHost.LocalIpAddresses"/> 先例，Server 不引用 WinHost 工程故独立实现）。
/// </summary>
public static class LocalIpv4Candidates
{
    /// <summary>
    /// 枚举本机 IPv4 候选（仅启用网卡的非回环地址，去重）；
    /// 私网（局域网）地址排在前、其余地址殿后——前端「origin 为 localhost / 无匹配时回退首个局域网 IPv4」的取首依据。
    /// </summary>
    public static IReadOnlyList<string> Enumerate()
    {
        var result = new List<string>();
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address.Address))
                    {
                        continue;
                    }

                    var text = address.Address.ToString();
                    if (!result.Contains(text))
                    {
                        result.Add(text);
                    }
                }
            }
        }
        catch
        {
            // 网络枚举失败不影响接口可用性（返回空列表，前端回退当前 origin 展示）
        }

        return result.OrderBy(ip => IsPrivateNetwork(ip) ? 0 : 1).ToList();
    }

    /// <summary>是否私网 IPv4（RFC1918：10/8、172.16/12、192.168/16）——局域网候选优先排序的判定依据。</summary>
    internal static bool IsPrivateNetwork(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            && (bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168));
    }
}
