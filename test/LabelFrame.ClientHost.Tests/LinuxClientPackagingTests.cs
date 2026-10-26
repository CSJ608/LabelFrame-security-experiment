using System.Xml.Linq;
using Xunit;

namespace LabelFrame.ClientHost.Tests;

/// <summary>验证 Linux 客户端镜像还原层的项目依赖输入。</summary>
public sealed class LinuxClientPackagingTests
{
    [Fact]
    public void Restore_layer_should_include_all_referenced_projects()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LabelFrame.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var dockerfile = File.ReadAllText(Path.Combine(root.FullName, "packaging/linux-client/Dockerfile"));
        var restoreOffset = dockerfile.IndexOf("RUN dotnet restore", StringComparison.Ordinal);
        Assert.True(restoreOffset >= 0);
        var restoreInputs = dockerfile[..restoreOffset];
        var pending = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(Path.Combine(root.FullName, "src/LabelFrame.WinHost/LabelFrame.WinHost.csproj"));
        while (pending.TryDequeue(out var project))
        {
            project = Path.GetFullPath(project);
            if (!visited.Add(project)) continue;
            var relative = Path.GetRelativePath(root.FullName, project).Replace('\\', '/');
            Assert.Contains($"COPY {relative} {relative[..(relative.LastIndexOf('/') + 1)]}", restoreInputs);
            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar);
                pending.Enqueue(Path.Combine(Path.GetDirectoryName(project)!, include));
            }
        }
    }
}
