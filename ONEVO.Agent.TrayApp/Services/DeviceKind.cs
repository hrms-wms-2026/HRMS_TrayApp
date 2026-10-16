namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.TrayApp.Interop;

/// <summary>Detects laptop vs desktop from the system battery (no battery → desktop).</summary>
public static class DeviceKind
{
    private const byte NoSystemBattery = 128;
    private const byte UnknownStatus = 255;

    public static string Display()
    {
        try
        {
            if (!NativeMethods.GetSystemPowerStatus(out var status))
                return "Windows PC";
            return status.BatteryFlag switch
            {
                NoSystemBattery => "Windows Desktop",
                UnknownStatus => "Windows PC",
                _ => "Windows Laptop",
            };
        }
        catch
        {
            return "Windows PC";
        }
    }
}
