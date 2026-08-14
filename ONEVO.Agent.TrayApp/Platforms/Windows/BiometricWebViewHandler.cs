namespace ONEVO.Agent.TrayApp.Platforms.Windows;

using System.Text.Json;
using Microsoft.Maui.Handlers;
using Microsoft.Web.WebView2.Core;
using ONEVO.Agent.TrayApp.Controls;
using WebView2 = global::Microsoft.UI.Xaml.Controls.WebView2;

/// <summary>
/// Hosts the packaged React FaceLivenessDetector build behind a WebView2 virtual host origin
/// (never file://, which restricts/blocks getUserMedia). Camera permission is allowed only for
/// the exact biometric origin. Session credentials are pushed into the JS runtime via
/// ExecuteScriptAsync and never touch Preferences/SQLite/logs on the native side either.
/// </summary>
public sealed class BiometricWebViewHandler : ViewHandler<BiometricWebView, WebView2>
{
    private const string VirtualHost = "biometric.onevo.local";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null // PascalCase — matches React bridge (AwsSessionId, etc.)
    };

    public static PropertyMapper<BiometricWebView, BiometricWebViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(BiometricWebView.SessionConfig)] = MapSessionConfig,
        };

    private bool _initialized;
    private BiometricSessionConfig? _pendingConfig;

    public BiometricWebViewHandler() : base(Mapper) { }

    protected override WebView2 CreatePlatformView() => new();

    private static async void MapSessionConfig(BiometricWebViewHandler handler, BiometricWebView view)
    {
        if (view.SessionConfig is null)
            return;

        handler._pendingConfig = view.SessionConfig;

        if (!handler._initialized)
        {
            await handler.InitializeAsync();
            handler._initialized = true;
        }
        else
        {
            await handler.PushSessionConfigAsync(view.SessionConfig);
        }
    }

    private async Task InitializeAsync()
    {
        await PlatformView.EnsureCoreWebView2Async();
        var core = PlatformView.CoreWebView2;

        core.Settings.AreDevToolsEnabled = false;

        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "biometric"),
            CoreWebView2HostResourceAccessKind.DenyCors);

        core.PermissionRequested += (_, args) =>
        {
            var isBiometricOrigin = args.Uri.StartsWith($"https://{VirtualHost}", StringComparison.Ordinal);
            var isCameraRequest = args.PermissionKind == CoreWebView2PermissionKind.Camera;
            args.State = (isBiometricOrigin && isCameraRequest)
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;
            args.Handled = true;
        };

        core.WebMessageReceived += (_, args) =>
        {
            BiometricCaptureOutcome? outcome;
            try
            {
                outcome = JsonSerializer.Deserialize<BiometricCaptureOutcome>(args.WebMessageAsJson, JsonOptions);
            }
            catch (JsonException)
            {
                outcome = new BiometricCaptureOutcome(false, "MALFORMED_BRIDGE_MESSAGE");
            }

            if (outcome is not null && VirtualView?.CaptureFinishedCommand is { } command && command.CanExecute(outcome))
                command.Execute(outcome);
        };

        core.NavigationCompleted += async (_, e) =>
        {
            if (!e.IsSuccess || _pendingConfig is null)
                return;

            await PushSessionConfigAsync(_pendingConfig);
        };

        core.Navigate($"https://{VirtualHost}/index.html");
    }

    private async Task PushSessionConfigAsync(BiometricSessionConfig config)
    {
        var core = PlatformView.CoreWebView2;
        if (core is null)
            return;

        var configJson = JsonSerializer.Serialize(config, JsonOptions);
        await core.ExecuteScriptAsync($"window.__onevoLivenessConfig = {configJson};");
    }

    protected override void DisconnectHandler(WebView2 platformView)
    {
        platformView.Close();
        base.DisconnectHandler(platformView);
    }
}
