using System.Diagnostics;
using System.IO;
using System.Windows;
using QrGuard.Core;

namespace QrGuard.Windows;

internal sealed class BrowserLauncher : ILinkLauncher
{
    public void Open(ValidatedHttpTarget target)
    {
        // The original validated URI is the document target, never a shell executable or command argument.
        using Process? process = Process.Start(new ProcessStartInfo(target.OriginalText) { UseShellExecute = true });
    }
}

internal sealed class UserClipboard : IClipboardAdapter
{
    public void Copy(ValidatedHttpTarget target) => Clipboard.SetText(target.OriginalText);
}

internal sealed class LocalPolicyFile(LocalPolicyProvider provider)
{
    private string? _path;
    public bool Select(string path)
    {
        if (!IsLocal(path)) { provider.Reject(PolicyFailure.Read); return false; }
        _path = Path.GetFullPath(path); return Reload();
    }
    public bool Reload()
    {
        if (_path is null) return true;
        try
        {
            if (!IsLocal(_path)) { provider.Reject(PolicyFailure.Read); return false; }
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return provider.TryLoad(stream);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { provider.Reject(PolicyFailure.Read); return false; }
    }
    private static bool IsLocal(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith("\\\\", StringComparison.Ordinal)) return false;
            var drive = new DriveInfo(Path.GetPathRoot(full)!);
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) return false;
            // Reject reparse traversal before opening: a local filename must not resolve into a remote share.
            for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
