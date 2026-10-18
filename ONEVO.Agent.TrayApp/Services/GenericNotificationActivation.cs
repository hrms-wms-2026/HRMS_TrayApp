namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;

/// <summary>
/// Toast activation arguments for generic notifications (<c>kind=generic;delivery={id};action={key}</c>, the
/// same semicolon-separated shape Windows App SDK produces for the inactivity prompt). A click only ever maps a
/// registered action to a URL built here from the configured portal - never a URL taken from the payload - and
/// the web app checks the user's access to the item it opens.
/// </summary>
public static class GenericNotificationActivation
{
    public const string OpenRelated = "open_related";
    private const string KindGeneric = "kind=generic";

    public static string BuildArguments(GenericNotificationPushPayload payload)
        => $"{KindGeneric};delivery={payload.DeliveryId};action={payload.ActionKey}";

    public static bool IsGeneric(string? args)
        => !string.IsNullOrWhiteSpace(args)
           && args.Split(';', StringSplitOptions.RemoveEmptyEntries).Contains(KindGeneric, StringComparer.Ordinal);

    /// <summary>The URL to open for a clicked generic toast, or null when the click has no action. Never throws.</summary>
    public static string? TryGetNavigationUrl(string? args, string portalBaseUrl)
    {
        if (!IsGeneric(args))
            return null;

        string? action = null;
        Guid? deliveryId = null;
        foreach (var pair in args!.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2)
                continue;
            if (parts[0] == "action")
                action = parts[1];
            else if (parts[0] == "delivery" && Guid.TryParse(parts[1], out var id))
                deliveryId = id;
        }

        if (deliveryId is null || action != OpenRelated)
            return null;
        if (!Uri.TryCreate(portalBaseUrl, UriKind.Absolute, out var portal)
            || (portal.Scheme != Uri.UriSchemeHttps && portal.Scheme != Uri.UriSchemeHttp))
            return null;

        return portalBaseUrl.TrimEnd('/') + "/inbox";
    }
}
