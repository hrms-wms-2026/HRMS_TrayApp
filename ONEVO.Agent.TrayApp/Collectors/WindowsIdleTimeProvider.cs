namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.TrayApp.Security;

/// <summary>
/// <see cref="IIdleTimeProvider"/> backed by the existing
/// <see cref="PrivacyScrubber.GetSecondsSinceLastInput"/> Win32 <c>GetLastInputInfo</c> wrapper
/// (§7.3, §8) — only an elapsed-seconds count ever crosses this boundary; no key codes, characters,
/// or coordinates.
/// </summary>
public sealed class WindowsIdleTimeProvider : IIdleTimeProvider
{
    public int GetIdleSeconds() => PrivacyScrubber.GetSecondsSinceLastInput();
}
