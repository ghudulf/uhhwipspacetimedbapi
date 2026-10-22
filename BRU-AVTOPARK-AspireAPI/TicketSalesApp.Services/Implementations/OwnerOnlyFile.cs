using System.Security.AccessControl;
using System.Security.Principal;

namespace TicketSalesApp.Services.Implementations;

/// <summary>Restricts secret storage to the current service account.</summary>
public static class OwnerOnlyFile
{
    public static void Restrict(string path)
    {
        var directory = Directory.Exists(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                (directory ? UnixFileMode.UserExecute : 0));
            return;
        }
        var user = WindowsIdentity.GetCurrent().User ?? throw new IOException("Cannot identify secret storage owner.");
        if (directory)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(user);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }
        else
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(user);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
    }

    public static void CreateDirectory(string path)
    {
        if (new DirectoryInfo(path).LinkTarget != null) throw new IOException("Secret directory must not be a symbolic link.");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Restrict(path);
    }
}
