using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.Versioning;

namespace LabelFrame.WinHost.Tests;

/// <summary>使用临时测试证书为隔离 MSI 样本签名。</summary>
[SupportedOSPlatform("windows")]
internal static class FixtureSigner
{
    public static void Sign(string path, string certificatePath)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=LabelFrame Isolated Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.3") }, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Cert));
        var type = Marshal.AllocHGlobal(16);
        Marshal.StructureToPtr(new Guid("000C10F1-0000-0000-C000-000000000046"), type, false);
        var name = Marshal.StringToHGlobalUni(path);
        var hash = Marshal.StringToHGlobalAnsi("2.16.840.1.101.3.4.2.1");
        var oid = Marshal.StringToHGlobalAnsi("1.3.6.1.4.1.311.2.1.4");
        var subject = new Subject { Size = (uint)Marshal.SizeOf<Subject>(), Type = type, File = new(-1), Name = name, Encoding = 65537, Algorithm = new Algorithm { Oid = hash } };
        if (!CryptAcquireContext(out subject.Provider, null, null, 24, 0xF0000000)) throw new InvalidDataException("provider");
        uint size = 0;
        CryptSIPCreateIndirectData(ref subject, ref size, IntPtr.Zero);
        var indirect = Marshal.AllocHGlobal((int)size);
        try
        {
            if (!CryptSIPCreateIndirectData(ref subject, ref size, indirect)) throw new InvalidDataException("indirect " + Marshal.GetLastWin32Error());
            uint encodedSize = 0;
            if (!CryptEncodeObjectEx(65537, oid, indirect, 0, IntPtr.Zero, null, ref encodedSize)) throw new InvalidDataException("encode size");
            var content = new byte[encodedSize];
            if (!CryptEncodeObjectEx(65537, oid, indirect, 0, IntPtr.Zero, content, ref encodedSize)) throw new InvalidDataException("encode");
            var cms = new SignedCms(new ContentInfo(new Oid("1.3.6.1.4.1.311.2.1.4"), content), false);
            cms.ComputeSignature(new CmsSigner(certificate) { DigestAlgorithm = new("2.16.840.1.101.3.4.2.1") });
            var signed = cms.Encode();
            if (!CryptSIPPutSignedDataMsg(ref subject, 65537, out _, (uint)signed.Length, signed)) throw new InvalidDataException("put " + Marshal.GetLastWin32Error());
        }
        finally
        {
            CryptReleaseContext(subject.Provider, 0);
            foreach (var pointer in new[] { type, name, hash, oid, indirect }) Marshal.FreeHGlobal(pointer);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public uint Size; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] private struct Algorithm { public IntPtr Oid; public Blob Parameters; }
    [StructLayout(LayoutKind.Sequential)] private struct Subject
    {
        public uint Size; public IntPtr Type, File, Name, Display; public uint Reserved1, Version;
        public IntPtr Provider; public Algorithm Algorithm; public uint Flags, Encoding, Reserved2, Capi, Security, Index, Choice; public IntPtr Additional, Client;
    }
    [DllImport("crypt32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptSIPCreateIndirectData(ref Subject subject, ref uint size, IntPtr indirect);
    [DllImport("crypt32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptEncodeObjectEx(uint encoding, IntPtr type, IntPtr data, uint flags, IntPtr parameters, byte[]? result, ref uint size);
    [DllImport("crypt32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptSIPPutSignedDataMsg(ref Subject subject, uint encoding, out uint index, uint size, byte[] signature);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptAcquireContext(out IntPtr provider, string? container, string? name, uint type, uint flags);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptReleaseContext(IntPtr provider, uint flags);
}


