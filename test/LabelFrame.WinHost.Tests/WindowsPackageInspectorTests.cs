using System.Runtime.InteropServices;
using LabelFrame.Updates;
using Xunit;

namespace LabelFrame.WinHost.Tests;

/// <summary>真实 MSI 的完整签名、产品读取与篡改拒绝验证。</summary>
[Collection(nameof(NativeUpdateTestGroup))]
public sealed class WindowsPackageInspectorTests
{
    [Fact]
    public async Task Real_msi_requires_signature_pin_and_untampered_signed_database()
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-msi-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "LabelFrame-Client-1.5.0.msi");
        var certificate = Path.Combine(root, "test-publisher.cer");
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Updates", "unsigned-client.msi"), path);
            await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsPackageInspector(ClientUpdateContract.CertificatePath).InspectAsync(path, Path.GetFileName(path)));
            FixtureSigner.Sign(path, certificate);
            var package = await new WindowsPackageInspector(certificate).InspectAsync(path, Path.GetFileName(path));
            Assert.Equal("1.5.0", package.Version);
            Assert.Equal("x64", package.Architecture);
            Assert.Equal("1.0.0", package.MinServerVersion);
            Assert.Equal("2.0.0", package.MaxServerVersion);
            Assert.Equal(10, package.RuntimeMajor);
            await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsPackageInspector(ClientUpdateContract.CertificatePath).InspectAsync(path, Path.GetFileName(path)));
            MutateProduct(path);
            await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsPackageInspector(certificate).InspectAsync(path, Path.GetFileName(path)));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void MutateProduct(string path)
    {
        dynamic installer = Activator.CreateInstance(Type.GetTypeFromProgID("WindowsInstaller.Installer")!)!;
        try
        {
            dynamic database = installer.OpenDatabase(path, 1);
            try
            {
                dynamic view = database.OpenView("UPDATE `Property` SET `Value` = 'modified' WHERE `Property` = 'ProductName'");
                try { view.Execute(); view.Close(); database.Commit(); }
                finally { Marshal.FinalReleaseComObject(view); }
            }
            finally { Marshal.FinalReleaseComObject(database); }
        }
        finally { Marshal.FinalReleaseComObject(installer); }
    }
}
