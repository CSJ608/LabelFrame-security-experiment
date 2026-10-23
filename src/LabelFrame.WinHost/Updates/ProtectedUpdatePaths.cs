using System.Security.AccessControl;
using System.Security.Principal;

namespace LabelFrame.WinHost.Updates;

/// <summary>更新执行器的固定安装路径与管理员保护检查。</summary>
internal static class ProtectedUpdatePaths
{
    public static string Root => Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!, "ClientUpdateState");
    public static string Executable => Path.Combine(AppContext.BaseDirectory, "LabelFrame.WinHost.exe");

    public static void Verify(string path)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var current = Path.GetFullPath(path);
        if (!current.StartsWith(programFiles + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("自动安装仅支持管理员部署在 Program Files 下的客户端。");
        while (current.Length >= programFiles.Length)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("更新路径不能包含重解析点。");
            var acl = Directory.Exists(current) ? (FileSystemSecurity)new DirectoryInfo(current).GetAccessControl()
                : new FileInfo(current).GetAccessControl();
            if (!Trusted(acl.GetOwner(typeof(SecurityIdentifier))?.Value))
                throw new InvalidDataException("更新路径所有者必须是管理员或系统。");
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
                const FileSystemRights writes = FileSystemRights.Write | FileSystemRights.Delete
                    | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | FileSystemRights.DeleteSubdirectoriesAndFiles;
                var sid = rule.IdentityReference.Value;
                if ((rule.FileSystemRights & writes) != 0 && !Trusted(sid))
                    throw new InvalidDataException("更新路径允许非管理员写入，请重新以管理员身份安装客户端。");
            }
            if (current.Equals(programFiles, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current) ?? throw new InvalidDataException("更新安装路径无效。");
        }
    }

    private static bool Trusted(string? sid) => sid is "S-1-5-18" or "S-1-5-32-544"
        or "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    public static void CreateRoot()
    {
        Verify(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        if (!Directory.Exists(Root))
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (var (sid, rights) in new[] { (WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl),
                (WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl),
                (WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute) })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), rights,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(Root).Create(security);
        }
        Verify(Root);
    }
}
