namespace ONEVO.Agent.TrayApp.Services;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Xml.Linq;
using ONEVO.Agent.TrayApp.Interop;

/// <summary>
/// Extracts each foreground app's own executable icon for display in the Top Applications
/// summary. Purely a local UI convenience: the rendered bitmap is cached in memory only and
/// never sent over IPC or written to disk — the executable path used to extract it is read
/// once, in-process, and discarded (matches PrivacyScrubber's "never path" rule for anything
/// that leaves this process).
/// </summary>
public sealed class AppIconCache : IAppIconCache
{
    private readonly ILogger<AppIconCache> _logger;
    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public AppIconCache(ILogger<AppIconCache> logger)
    {
        _logger = logger;
    }

    public ImageSource? GetIcon(string processName) =>
        _cache.TryGetValue(Normalize(processName), out var icon) ? icon : null;

    public void TryCacheFromForegroundWindow(IntPtr hwnd, string processName)
    {
        var key = Normalize(processName);
        if (_cache.ContainsKey(key))
            return; // already attempted — success or permanent miss, don't retry every tick

        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) { _cache[key] = null; return; }

            using var process = Process.GetProcessById((int)pid);
            var exePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath)) { _cache[key] = null; return; }

            var bytes = TryResolvePackagedAppLogo(exePath) ?? TryExtractAssociatedIcon(exePath);
            if (bytes is null) { _cache[key] = null; return; }

            _cache[key] = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Icon extraction failed for {Process}", processName);
            _cache[key] = null;
        }
    }

    /// <summary>Fast path for ordinary Win32 executables. Also the fallback for packaged apps
    /// whose manifest logo couldn't be resolved — usually returns a generic placeholder icon
    /// for those (the packaged stub .exe rarely carries its own embedded icon resource), but
    /// that's still better than no icon at all.</summary>
    private static byte[]? TryExtractAssociatedIcon(string exePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;

            using var bitmap = icon.ToBitmap();
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Packaged (MSIX/UWP) apps ship a thin stub .exe with no embedded resource icon —
    /// <see cref="Icon.ExtractAssociatedIcon"/> returns a generic placeholder for these. Their
    /// real logo is declared in the package's AppxManifest.xml and shipped as scaled PNG assets
    /// alongside it; this resolves and reads that file directly.</summary>
    private static byte[]? TryResolvePackagedAppLogo(string exePath)
    {
        try
        {
            var packageDir = FindPackageRootDirectory(exePath);
            if (packageDir is null) return null;

            var manifestPath = Path.Combine(packageDir, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) return null;

            XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
            var doc = XDocument.Load(manifestPath);
            var logoRelative = doc.Descendants(uap + "VisualElements")
                .Select(e => (string?)e.Attribute("Square44x44Logo"))
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (logoRelative is null) return null;

            var logoFile = ResolveScaledAsset(packageDir, logoRelative);
            return logoFile is null ? null : File.ReadAllBytes(logoFile);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Walks up from the exe's directory looking for AppxManifest.xml — the package
    /// root — bounded to a few levels since packaged exes are never nested deeply. Skips the
    /// walk entirely for exes outside WindowsApps to avoid wasted work for ordinary apps.</summary>
    private static string? FindPackageRootDirectory(string exePath)
    {
        if (!exePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
            return null;

        var dir = Path.GetDirectoryName(exePath);
        for (var i = 0; i < 4 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "AppxManifest.xml")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>The manifest's logo path (e.g. "Assets\AppList.png") is a logical name — the
    /// actual asset on disk is one or more resource-qualified variants (AppList.scale-200.png,
    /// AppList.targetsize-64.png, etc). Prefers a plain, reasonably sized, non-themed variant.</summary>
    private static string? ResolveScaledAsset(string packageDir, string logicalRelativePath)
    {
        var dir = Path.Combine(packageDir, Path.GetDirectoryName(logicalRelativePath) ?? string.Empty);
        if (!Directory.Exists(dir)) return null;

        var baseName = Path.GetFileNameWithoutExtension(logicalRelativePath);
        var ext = Path.GetExtension(logicalRelativePath);

        var candidates = Directory.GetFiles(dir, $"{baseName}*{ext}");
        if (candidates.Length == 0) return null;

        string[] preferredSuffixes = ["", ".targetsize-64", ".targetsize-48", ".targetsize-96", ".scale-200", ".scale-100"];
        foreach (var suffix in preferredSuffixes)
        {
            var match = candidates.FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), baseName + suffix, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return candidates.FirstOrDefault(f => !f.Contains("altform", StringComparison.OrdinalIgnoreCase))
            ?? candidates[0];
    }

    private static string Normalize(string processName)
    {
        var n = processName.Trim().ToLowerInvariant();
        return n.EndsWith(".exe", StringComparison.Ordinal) ? n : n + ".exe";
    }
}
