using LabelFrame.AndroidHost.Updates;
using Xunit;

namespace LabelFrame.AndroidHost.Tests;

public sealed class UpdateCacheTests
{
    [Fact]
    public void RemoveCandidateBeforeFirstDownloadDoesNotRequireDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LabelFrame-update-cache-" + Guid.NewGuid().ToString("N"));
        UpdateDownloadClient.RemoveCandidate(directory);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void RemoveCandidatePreservesOtherFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LabelFrame-update-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "candidate.apk"), "candidate");
            File.WriteAllText(Path.Combine(directory, "diagnostic.txt"), "keep");
            UpdateDownloadClient.RemoveCandidate(directory);
            UpdateDownloadClient.RemoveCandidate(directory);
            Assert.False(File.Exists(Path.Combine(directory, "candidate.apk")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(directory, "diagnostic.txt")));
        }
        finally { Directory.Delete(directory, true); }
    }
}
