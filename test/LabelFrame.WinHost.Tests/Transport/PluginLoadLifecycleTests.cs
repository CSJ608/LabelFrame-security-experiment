using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// 外置插件加载生命周期回归（/ DESIGN 「加载生命周期」）：
/// 锚定缺陷时序「启动扫描（ALC 根只经注册表持有，加载结果即刻弃置）→ 多轮强制 GC + 延迟 →
/// 首用触发依赖解析」不再抛 FileLoadException(VerifyIsAlive)——官方 Zebra 插件生产同构包
/// （真实插件 DLL + SDK 全套伴生闭包）经三层校验安装 + 目录装配，首用走 TestAsync
/// （加载失败发生在网络动作之前，不可达端口得到干净连接失败即证明加载路径已过）。
/// </summary>
public class PluginLoadLifecycleTests
{
    [Fact]
    public async Task Official_plugin_first_use_after_scan_gc_and_delay_should_not_throw()
    {
        var pluginsDir = Path.Combine(Path.GetTempPath(), $"lfplugin-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pluginsDir);
        try
        {
            // 安装（三层校验真实路径：PluginProbe 单目录单 ALC 预检 + 显式 Unload 同路径覆盖）
            var registry = new TransportPluginRegistry();
            var installer = new PluginInstaller(pluginsDir, registry, TextWriter.Null);
            await installer.InstallAsync(
                new MemoryStream(OfficialPackageFactory.Build("0.27.0")),
                "labelframe-transport-zebra-0.27.0.lfplugin",
                CancellationToken.None);

            // 「重启」宿主装配（WinHostApp 同构）：ALC 根只经注册表强持有，加载结果引用即刻离开作用域
            LoadAndRegister(pluginsDir, registry);
            Assert.True(registry.GetPlugin("labelframe-transport-zebra")!.IsExternal);

            // 缺陷时序窗口：多轮强制 GC + 延迟（旧加载器下插件 ALC 被 GC 发起卸载 → 首用 VerifyIsAlive 抛）
            ForceCollect(5);
            await Task.Delay(500);

            // 首用：连接测试（不可达端口——若 ALC 状态机破坏，此处抛 FileLoadException 而非返回消息）
            var context = new TransportPluginContext(TextWriter.Null, pluginsDir);
            var first = await CreateAndTestAsync(registry, context);
            Assert.Contains("连接测试失败", first);

            // 重复性：再一轮 GC + 延迟后再用（持续可用，非一次性侥幸）
            ForceCollect(3);
            await Task.Delay(200);
            var second = await CreateAndTestAsync(registry, context);
            Assert.Contains("连接测试失败", second);
        }
        finally
        {
            try
            {
                Directory.Delete(pluginsDir, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // 字节加载不锁文件；残余失败留给系统临时目录
            }
        }
    }

    /// <summary>宿主启动装配同构：目录加载 → 注册表强持有（含 ALC 根）——加载结果引用不外泄（生产形态）。</summary>
    private static void LoadAndRegister(string pluginsDir, TransportPluginRegistry registry)
    {
        foreach (var discovered in PluginDirectoryLoader.Load(pluginsDir, TextWriter.Null))
        {
            registry.RegisterExternal(discovered.Plugin, discovered.AssemblyPath, TextWriter.Null, discovered.LoadContext);
        }
    }

    /// <summary>经注册表装配传输并执行连接测试（TransportManager.TestAsync 同一入口；返回错误消息 = 加载路径已过）。</summary>
    private static async Task<string?> CreateAndTestAsync(TransportPluginRegistry registry, ITransportPluginContext context)
    {
        var transport = registry.CreateTransport(
            "labelframe-transport-zebra",
            new TransportPluginParameters(new Dictionary<string, string>
            {
                ["kind"] = "Tcp",
                ["host"] = "127.0.0.1",
                ["port"] = "1", // 不可达端口：加载失败发生在网络动作之前
            }),
            context);
        return await ((ITestableTransport)transport).TestAsync();
    }

    /// <summary>多轮强制 GC（含终结器等待）——主动触发 collectible ALC 的卸载判定，而非等待自然 GC。</summary>
    private static void ForceCollect(int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }
}
