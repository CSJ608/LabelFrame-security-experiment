using System.Net;
using System.Net.Sockets;
using LabelFrame.Server;
using Xunit;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 本机 IPv4 候选枚举单测（· ：下载中心「连接信息」卡）：
/// 枚举结果全部为合法非回环 IPv4 且去重；私网（RFC1918）判定与「私网优先」排序可离线断言。
/// </summary>
public sealed class LocalIpv4CandidatesTests
{
    [Theory]
    [InlineData("10.0.0.5", true)]      // 10/8
    [InlineData("10.255.255.255", true)]
    [InlineData("172.16.0.1", true)]    // 172.16/12 下界
    [InlineData("172.31.255.254", true)] // 172.16/12 上界
    [InlineData("192.168.1.5", true)]   // 192.168/16
    [InlineData("172.32.0.1", false)]   // 172.16/12 之外
    [InlineData("172.15.255.254", false)]
    [InlineData("8.8.8.8", false)]      // 公网
    [InlineData("127.0.0.1", false)]    // 回环不属私网候选
    [InlineData("not-an-ip", false)]    // 非法输入
    [InlineData("::1", false)]          // IPv6 不属本判定面
    public void IsPrivateNetwork_should_classify_rfc1918(string ip, bool expected)
    {
        Assert.Equal(expected, LocalIpv4Candidates.IsPrivateNetwork(ip));
    }

    [Fact]
    public void Enumerate_should_return_valid_deduped_non_loopback_ipv4()
    {
        var candidates = LocalIpv4Candidates.Enumerate();

        var seen = new HashSet<string>();
        foreach (var ip in candidates)
        {
            // 全部可解析为 IPv4、非回环、无重复
            Assert.True(IPAddress.TryParse(ip, out var address), $"候选 {ip} 应为合法 IP");
            Assert.Equal(AddressFamily.InterNetwork, address.AddressFamily);
            Assert.False(IPAddress.IsLoopback(address), $"候选 {ip} 不应为回环地址");
            Assert.True(seen.Add(ip), $"候选 {ip} 重复出现");
        }
    }

    [Fact]
    public void Enumerate_should_order_private_addresses_first()
    {
        var candidates = LocalIpv4Candidates.Enumerate();

        // 私网前缀单调：一旦出现非私网候选，其后不得再出现私网候选（稳定排序下同组保序）
        var seenPublic = false;
        foreach (var ip in candidates)
        {
            if (LocalIpv4Candidates.IsPrivateNetwork(ip))
            {
                Assert.False(seenPublic, $"私网候选 {ip} 出现在公网候选之后，排序违反「首个局域网 IPv4 优先」");
            }
            else
            {
                seenPublic = true;
            }
        }
    }
}
