using LabelFrame.Bootstrapper.Manifest;

namespace LabelFrame.Bootstrapper.Topology;

/// <summary>目标安装位置描述（确认页展示；目录约定对齐 docs/DEPLOY.md §2 引导程序 / §3 MSI / §5 Ubuntu / §6 管理界面 / §7 插件分发）。问卷收敛后仅服务本机三角色——Linux 路径描述随 server-linux 移出问卷删除（Linux 侧承接 = install.sh）。</summary>
/// <remarks>问卷阶段只描述不落位；实际安装 / 修改语义由专项 6/8定案，届时此映射升级为结构化路径。</remarks>
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
