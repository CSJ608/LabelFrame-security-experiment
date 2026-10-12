using System.IO.Compression;
using System.Text;
using LabelFrame.Core.Transport.Plugins.Package;

namespace LabelFrame.WinHost.Tests.Transport;

/// <summary>
/// 测试用官方 Zebra 插件包工厂：镜像 scripts/build-zebra-plugin.ps1 的产物形态——
/// 真实插件程序集 + Zebra SDK 全套伴生依赖（取自测试 bin 的完整闭包），排除「宿主必带」程序集，
/// 使安装 / 装配用例与生产 .lfplugin 同构（CI 内验证生产包形状， 装配面代理）。
/// </summary>
internal static class OfficialPackageFactory
{
    /// <summary>宿主必带程序集排除清单（与打包脚本一致；LabelFrame.Core 类型统一必须由宿主提供）。</summary>
    private static readonly string[] HostProvided =
    [
        "LabelFrame.Core.dll",
        "Microsoft.Data.Sqlite.dll",
        "SQLitePCLRaw.core.dll",
        "SQLitePCLRaw.provider.e_sqlite3.dll",
        "DocumentFormat.OpenXml.dll",
        "DocumentFormat.OpenXml.Framework.dll",
        "System.IO.Packaging.dll",
        "TemplateFrame.dll",
        "TemplateFrame.Excel.Simple.dll",
        "libSkiaSharp.dll",
    ];

    /// <summary>构建官方插件包字节（manifest.json + 插件 DLL + SDK 伴生闭包）。</summary>
    public static byte[] Build(string version)
    {
        var pluginDir = Path.GetDirectoryName(typeof(LabelFrame.TransportPlugin.Zebra.ZebraTransportPlugin).Assembly.Location)
            ?? throw new InvalidOperationException("无法定位插件程序集目录。");
        var dlls = Directory.GetFiles(pluginDir, "*.dll")
            .Where(file => !HostProvided.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => IsPluginClosure(Path.GetFileName(file), pluginDir))
            .ToList();
        if (!dlls.Any(file => string.Equals(Path.GetFileName(file), "LabelFrame.TransportPlugin.Zebra.dll", StringComparison.OrdinalIgnoreCase))
            || !dlls.Any(file => string.Equals(Path.GetFileName(file), "ZebraPrinterSdk.dll", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("插件闭包不完整（缺主 DLL 或 Zebra SDK）。");
        }

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry(PluginPackageReader.ManifestFileName);
            using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            {
                w.Write($$"""{"pluginId":"labelframe-transport-zebra","name":"Zebra 品牌传输（官方）","version":"{{version}}","author":"LabelFrame"}""");
            }

            foreach (var dll in dlls)
            {
                var entry = zip.CreateEntry(Path.GetFileName(dll));
                using var target = entry.Open();
                using var source = File.OpenRead(dll);
                source.CopyTo(target);
            }
        }

        return ms.ToArray();
    }

    /// <summary>是否属插件依赖闭包（测试 bin 混入测试 / 宿主程序集——按打包脚本同款白名单语义筛 SDK 闭包与基础库）。</summary>
    private static bool IsPluginClosure(string fileName, string pluginDir)
    {
        if (fileName.Equals("LabelFrame.TransportPlugin.Zebra.dll", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Zebra SDK 伴生闭包（打包脚本产出的确定性集合；测试 bin 中同名的才是伴生，宿主 / 测试自有程序集不带这些名）
        string[] sdkClosure =
        [
            "ZebraPrinterSdk.dll", "SdkApi.Core.dll", "SdkApi.Desktop.dll", "SdkApi.Desktop.Usb.dll",
            "BouncyCastle.Crypto.dll", "FluentFTP.dll", "SharpSnmpLib.dll", "Newtonsoft.Json.dll", "Csv.dll",
            "System.Drawing.Common.dll", "System.Private.Windows.Core.dll", "System.Private.Windows.GdiPlus.dll",
            "Microsoft.Win32.SystemEvents.dll", "Microsoft.Windows.SDK.NET.dll", "WinRT.Runtime.dll",
            "System.CodeDom.dll", "System.Management.dll",
            "Microsoft.Extensions.Configuration.Abstractions.dll", "Microsoft.Extensions.Configuration.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll", "Microsoft.Extensions.DependencyInjection.dll",
            "Microsoft.Extensions.DependencyModel.dll", "Microsoft.Extensions.Logging.Abstractions.dll",
            "Microsoft.Extensions.Logging.dll", "Microsoft.Extensions.Options.dll", "Microsoft.Extensions.Primitives.dll",
            "Microsoft.IO.RecyclableMemoryStream.dll", "SkiaSharp.dll",
        ];
        return sdkClosure.Contains(fileName, StringComparer.OrdinalIgnoreCase);
    }
}
