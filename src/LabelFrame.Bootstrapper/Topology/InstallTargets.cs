using LabelFrame.Bootstrapper.Manifest;

namespace LabelFrame.Bootstrapper.Topology;

/// <summary>目标安装位置描述（确认页展示用文案，非实际落位路径；目录约定对齐 docs/DEPLOY.md）。</summary>
public static class InstallTargets
{
    /// <summary>按组件条目与角色给出目标安装位置描述。</summary>
    public static string Describe(ManifestComponent component, TopologyPreset preset)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return component.Type switch
        {
            "msi" => component.Id switch
            {
                "server-msi" => $"{programFiles}\\LabelFrame\\Server（Windows 服务 LabelFrameServer；数据目录 {programData}\\LabelFrame\\server）",
                "client-msi" => $"{programFiles}\\LabelFrame\\Client",
                _ => $"{programFiles}\\LabelFrame\\{component.Id}",
            },
            "webui-zip" => $"{programData}\\LabelFrame\\server\\plugins\\web-ui（放入即生效，无需重启）",
            "archive" => $"/opt/labelframe/{component.Id}",
            "lfplugin" => $"{programData}\\LabelFrame\\Client\\plugins\\{BrandPluginMap.PluginIdOf(TopologyResolver.BrandIdOf(component.Id)) ?? component.Id}",
            "runtime" => component.Id switch
            {
                "runtime-desktop" => ".NET Desktop Runtime（系统级全局安装）",
                "runtime-aspnetcore" => "ASP.NET Core Runtime（系统级全局安装）",
                "runtime-webview2" => "WebView2 Evergreen 运行时（系统级全局安装）",
                _ => "系统运行时（系统级全局安装）",
            },
            _ => $"{programData}\\LabelFrame\\{component.Id}",
        };
    }
}
