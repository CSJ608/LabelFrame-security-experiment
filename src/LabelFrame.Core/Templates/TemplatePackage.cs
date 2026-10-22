using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.Core.Templates;

/// <summary>
/// 模板包：契约 + 版式 + 静态图片资源，可导入导出（zip）。
/// 模板按项目 / 客户分组（Group）。
/// </summary>
public sealed class TemplatePackage
{
    /// <summary>模板名（库内唯一）。</summary>
    public required string Name { get; init; }

    /// <summary>存储内不透明 uid（null 或空白 = 新建由存储分配；带 uid 保存即按行 upsert——行不存在则按该 uid 新建，改名不换 uid）。</summary>
    public string? Uid { get; init; }

    /// <summary>分组（项目 / 客户）。</summary>
    public required string Group { get; init; }

    /// <summary>契约。</summary>
    public required LabelContract Contract { get; init; }

    /// <summary>版式。</summary>
    public required LabelLayout Layout { get; init; }

    /// <summary>图片资源（键 → 图片字节，PNG / JPEG）。</summary>
    public IReadOnlyDictionary<string, byte[]> Images { get; init; } = new Dictionary<string, byte[]>();

    /// <summary>测试数据（键 → 值，PC / PDA 打印测试共用；可选，向后兼容）。</summary>
    public IReadOnlyDictionary<string, string> TestData { get; init; } = new Dictionary<string, string>();
}