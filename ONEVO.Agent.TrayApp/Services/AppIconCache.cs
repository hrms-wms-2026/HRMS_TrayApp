namespace ONEVO.Agent.TrayApp.Services;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
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

            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) { _cache[key] = null; return; }

            using var bitmap = icon.ToBitmap();
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            var bytes = ms.ToArray();
            _cache[key] = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Icon extraction failed for {Process}", processName);
            _cache[key] = null;
        }
    }

    private static string Normalize(string processName)
    {
        var n = processName.Trim().ToLowerInvariant();
        return n.EndsWith(".exe", StringComparison.Ordinal) ? n : n + ".exe";
    }
}
