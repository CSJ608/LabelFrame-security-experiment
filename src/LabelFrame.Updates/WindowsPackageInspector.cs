using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace LabelFrame.Updates;

/// <summary>使用固定发布者与 Windows MSI SIP 验证客户端安装包。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPackageInspector(string certificatePath) : IClientUpdatePackageInspector
{
    public async Task<ClientUpdatePackage> InspectAsync(string path, string fileName, CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > ClientUpdateContract.MaxPackageBytes) throw new InvalidDataException("安装包大小超出自动升级限制。");
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        var publisher = VerifySignature(path, certificatePath);
        var properties = ReadProperties(path, out var architecture);
        return ClientPackageMetadata.Validate(properties, architecture, fileName, publisher, sha256, file.Length);
    }

    public static string VerifySignature(string path, string certificatePath)
    {
        if (!MsiNative.CryptSIPRetrieveSubjectGuid(path, IntPtr.Zero, out var guid)
            || guid != new Guid("000C10F1-0000-0000-C000-000000000046"))
            throw new InvalidDataException("客户端更新必须是有效的 Windows Installer 安装包。");
        var type = Marshal.AllocHGlobal(16);
        var name = Marshal.StringToHGlobalUni(path);
        var subject = new MsiNative.Subject { Size = (uint)Marshal.SizeOf<MsiNative.Subject>(), Type = type, File = new IntPtr(-1), Name = name, Encoding = 65537 };
        Marshal.StructureToPtr(guid, type, false);
        try
        {
            if (!MsiNative.CryptAcquireContext(out subject.Provider, null, null, 24, 0xF0000000)) throw new Win32Exception();
            uint length = 0;
            if (!MsiNative.CryptSIPGetSignedDataMsg(ref subject, out var encoding, 0, ref length, null) || length is 0 or > 1024 * 1024)
                throw new InvalidDataException("安装包没有可验证的发布者签名。");
            var message = new byte[length];
            if (!MsiNative.CryptSIPGetSignedDataMsg(ref subject, out encoding, 0, ref length, message)) throw new Win32Exception();
            var (publisher, content) = PackageSignature.Verify(message, certificatePath);
            var objectId = Marshal.StringToHGlobalAnsi("1.3.6.1.4.1.311.2.1.4");
            IntPtr indirect;
            try
            {
                if (!MsiNative.CryptDecodeObjectEx(encoding, objectId, content, (uint)content.Length,
                    0x8000, IntPtr.Zero, out indirect, out _)) throw new Win32Exception();
            }
            finally { Marshal.FreeHGlobal(objectId); }
            try
            {
                subject.Algorithm = Marshal.PtrToStructure<MsiNative.Indirect>(indirect).Algorithm;
                if (Marshal.PtrToStringAnsi(subject.Algorithm.Oid) != "2.16.840.1.101.3.4.2.1"
                    || !MsiNative.CryptSIPVerifyIndirectData(ref subject, indirect))
                    throw new InvalidDataException("安装包内容摘要校验失败，文件可能损坏或被修改。");
            }
            finally { MsiNative.LocalFree(indirect); }
            return publisher;
        }
        catch (CryptographicException ex) { throw new InvalidDataException("安装包签名校验失败。", ex); }
        finally
        {
            if (subject.Provider != IntPtr.Zero) MsiNative.CryptReleaseContext(subject.Provider, 0);
            Marshal.FreeHGlobal(type);
            Marshal.FreeHGlobal(name);
        }
    }

    public static IReadOnlyDictionary<string, string> ReadProperties(string path, out string architecture)
    {
        MsiNative.Check(MsiNative.MsiOpenDatabase(path, IntPtr.Zero, out var database));
        try
        {
            MsiNative.Check(MsiNative.MsiDatabaseOpenView(database, "SELECT `Property`, `Value` FROM `Property`", out var view));
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                MsiNative.Check(MsiNative.MsiViewExecute(view, 0));
                while (true)
                {
                    var status = MsiNative.MsiViewFetch(view, out var record);
                    if (status == 259) break;
                    MsiNative.Check(status);
                    try
                    {
                        if (result.Count >= 1024) throw new InvalidDataException("安装包元数据条目过多。");
                        result.Add(MsiNative.GetString(record, 1), MsiNative.GetString(record, 2));
                    }
                    finally { _ = MsiNative.MsiCloseHandle(record); }
                }
            }
            finally { _ = MsiNative.MsiCloseHandle(view); }
            MsiNative.Check(MsiNative.MsiGetSummaryInformation(database, null, 0, out var summary));
            try
            {
                var template = new char[256];
                uint length = 255;
                MsiNative.Check(MsiNative.MsiSummaryInfoGetProperty(summary, 7, out _, out _, out _, template, ref length));
                architecture = new string(template, 0, (int)length).Split(';')[0];
            }
            finally { _ = MsiNative.MsiCloseHandle(summary); }
            return result;
        }
        finally { _ = MsiNative.MsiCloseHandle(database); }
    }
}

/// <summary>Windows Installer 与签名内容验证的原生边界。</summary>
[SupportedOSPlatform("windows")]
internal static class MsiNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Blob { public uint Size; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] internal struct Algorithm { public IntPtr Oid; public Blob Parameters; }
    [StructLayout(LayoutKind.Sequential)] internal struct Indirect { public Algorithm Data, Algorithm; public Blob Digest; }
    [StructLayout(LayoutKind.Sequential)] internal struct Subject
    {
        public uint Size; public IntPtr Type, File, Name, Display; public uint Reserved1, Version;
        public IntPtr Provider; public Algorithm Algorithm;
        public uint Flags, Encoding, Reserved2, Capi, Security, Index, Choice; public IntPtr Additional, Client;
    }
    internal static void Check(uint status) { if (status != 0) throw new InvalidDataException($"无法读取 Windows Installer 安装包（{status}）。"); }
    internal static string GetString(uint record, uint field)
    {
        uint size = 0;
        var status = MsiRecordGetString(record, field, null, ref size);
        if (status is not (0 or 234)) Check(status);
        if (size > 8192) throw new InvalidDataException("安装包元数据超出读取限制。");
        var buffer = new char[checked((int)size + 1)];
        size++;
        Check(MsiRecordGetString(record, field, buffer, ref size));
        return new string(buffer, 0, (int)size);
    }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CryptSIPRetrieveSubjectGuid(string path, IntPtr file, out Guid guid);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CryptSIPGetSignedDataMsg(ref Subject subject, out uint encoding, uint index, ref uint length, byte[]? data);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CryptDecodeObjectEx(uint encoding, IntPtr type, byte[] data, uint length, uint flags, IntPtr parameters, out IntPtr result, out uint size);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CryptSIPVerifyIndirectData(ref Subject subject, IntPtr indirect);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CryptAcquireContext(out IntPtr provider, string? container, string? name, uint type, uint flags);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CryptReleaseContext(IntPtr provider, uint flags);
    [DllImport("kernel32.dll")] internal static extern IntPtr LocalFree(IntPtr ptr);
    [DllImport("msi.dll", EntryPoint = "MsiOpenDatabaseW", CharSet = CharSet.Unicode)] internal static extern uint MsiOpenDatabase(string path, IntPtr persist, out uint database);
    [DllImport("msi.dll", EntryPoint = "MsiDatabaseOpenViewW", CharSet = CharSet.Unicode)] internal static extern uint MsiDatabaseOpenView(uint database, string query, out uint view);
    [DllImport("msi.dll")] internal static extern uint MsiViewExecute(uint view, uint record);
    [DllImport("msi.dll")] internal static extern uint MsiViewFetch(uint view, out uint record);
    [DllImport("msi.dll")] internal static extern uint MsiCloseHandle(uint handle);
    [DllImport("msi.dll", EntryPoint = "MsiRecordGetStringW", CharSet = CharSet.Unicode)] internal static extern uint MsiRecordGetString(uint record, uint field, [Out] char[]? value, ref uint size);
    [DllImport("msi.dll", EntryPoint = "MsiGetSummaryInformationW", CharSet = CharSet.Unicode)] internal static extern uint MsiGetSummaryInformation(uint database, string? path, uint update, out uint summary);
    [DllImport("msi.dll", EntryPoint = "MsiSummaryInfoGetPropertyW", CharSet = CharSet.Unicode)] internal static extern uint MsiSummaryInfoGetProperty(uint summary, uint property, out uint type, out int number, out System.Runtime.InteropServices.ComTypes.FILETIME time, [Out] char[] value, ref uint size);
}
