using Xunit;

namespace LabelFrame.Bootstrapper.Tests;

/// <summary>验证发布清单制品的根目录消费路径。</summary>
public sealed class ReleaseManifestArtifactTests
{
    [Fact]
    public void Consumers_should_use_artifact_root_without_source_directory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LabelFrame.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var workflow = File.ReadAllText(Path.Combine(root.FullName, ".github/workflows/release.yml"));
        Assert.Contains("-InstallManifestPath inputs/install-manifest.json", workflow);
        Assert.Contains("Copy-Item manifest-inputs/install-manifest.json release/install-manifest.json", workflow);
        Assert.Contains("Copy-Item manifest-inputs/latest.json release/latest.json", workflow);
        Assert.DoesNotContain("inputs/release/", workflow);
        Assert.Contains("-VerifyOnly", workflow);
    }
}
