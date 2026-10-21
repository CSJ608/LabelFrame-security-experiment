using System.Net;

namespace LabelFrame.Server;

/// <summary>受信代理解析结果：生效配置原文、是否全量信任、精确 IP 与 CIDR 网段集合。</summary>
public sealed class TrustedProxySet
{
    /// <summary>生效配置原文（首尾空白已去除；/api/server/info 回显用）。</summary>
    public string Raw { get; }

    /// <summary>是否「*」全量信任。</summary>
    public bool AllowAll { get; }

    /// <summary>精确 IP 形式的受信代理。</summary>
    public IReadOnlyList<IPAddress> Proxies { get; }

    /// <summary>CIDR 网段形式的受信代理。</summary>
    public IReadOnlyList<System.Net.IPNetwork> Networks { get; }

    public TrustedProxySet(string raw, bool allowAll, IReadOnlyList<IPAddress> proxies, IReadOnlyList<System.Net.IPNetwork> networks)
    {
        Raw = raw;
        AllowAll = allowAll;
        Proxies = proxies;
        Networks = networks;
    }

    /// <summary>判断地址是否受信（全量信任恒为真；否则按精确 IP 与网段匹配，地址族不一致不匹配）。</summary>
    public bool Contains(IPAddress address)
        => AllowAll || Proxies.Any(proxy => proxy.Equals(address)) || Networks.Any(network => network.Contains(address));
}

/// <summary>受信代理列表解析：逗号分隔 IP / CIDR 条目，「*」＝全量信任；非法条目逐条告警跳过、合法条目仍生效；无任何合法条目返回 null（视为关闭）。</summary>
public static class TrustedProxiesParser
{
    /// <summary>解析受信代理原始配置；null / 空白或解析后无任何合法条目返回 null（未启用）。</summary>
    public static TrustedProxySet? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        var proxies = new List<IPAddress>();
        var networks = new List<System.Net.IPNetwork>();
        var allowAll = false;
        foreach (var entry in trimmed.Split(','))
        {
            var item = entry.Trim();
            if (item.Length == 0)
            {
                continue;
            }

            if (item == "*")
            {
                allowAll = true;
            }
            else if (TryParseEntry(item, out var proxy, out var network))
            {
                if (proxy is not null)
                {
                    proxies.Add(proxy);
                }
                else
                {
                    networks.Add(network);
                }
            }
            else
            {
                Console.WriteLine(
                    $"[LabelFrame] 受信代理条目无效（LABELFRAME_SERVER_TRUSTED_PROXIES 条目「{item}」）：需为 IP 或 CIDR（如 172.16.0.5、172.16.0.0/12）或「*」，已跳过该条目。");
            }
        }

        // 不变量：解析后无任何合法条目视为关闭——配置仅含非法条目时不得保留任何转发头信任
        return allowAll
            ? new TrustedProxySet(trimmed, true, [], [])
            : proxies.Count + networks.Count == 0 ? null : new TrustedProxySet(trimmed, false, proxies, networks);
    }

    private static bool TryParseEntry(string item, out IPAddress? proxy, out System.Net.IPNetwork network)
    {
        proxy = null;
        network = default;
        var separator = item.IndexOf('/');
        if (separator < 0)
        {
            if (IPAddress.TryParse(item, out var address))
            {
                proxy = address;
                return true;
            }

            return false;
        }

        return System.Net.IPNetwork.TryParse(item, out network);
    }
}
