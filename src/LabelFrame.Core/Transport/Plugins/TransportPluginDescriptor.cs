namespace LabelFrame.Core.Transport.Plugins;

/// <summary>已装配插件描述（注册表列表项；API 返回给前端驱动表单）。</summary>
public sealed record TransportPluginDescriptor(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<TransportParameterSpec> Parameters,
    bool IsExternal = false,
    string? AssemblyPath = null,
    bool SupportsDocumentCompile = false,
    string? DisplayNameKey = null,
    string? DescriptionKey = null);
