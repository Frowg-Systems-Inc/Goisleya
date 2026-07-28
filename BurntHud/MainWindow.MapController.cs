using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.WebSockets;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Isley.Telemetry;
using Microsoft.Web.WebView2.Core;

namespace Isley;

public partial class MainWindow
{
    private string? _mapControllerScript;

    private async Task InstallPlayerFollowAsync()
    {
        if (LiveMapWebView.CoreWebView2 is null)
        {
            return;
        }

        string script;
        try
        {
            script = _mapControllerScript ??= await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Map", "isley-map-controller.js"));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            _followControllerInstalled = false;
            UpdateFollowButton(following: true, markerAvailable: false);
            return;
        }

        try
        {
            await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(script);
            _followControllerInstalled = true;
            await ApplyMapOptionsAsync();
            await SyncTerrainRoadNetworkAsync();
        }
        catch
        {
            _followControllerInstalled = false;
            UpdateFollowButton(following: true, markerAvailable: false);
        }
    }

    private async Task EnsureFollowControllerAsync()
    {
        if (!LiveMapServicesActive)
        {
            return;
        }

        if (LiveMapWebView.CoreWebView2 is null)
        {
            await InitializeLiveMapAsync();
        }

        if (!_followControllerInstalled)
        {
            await InstallPlayerFollowAsync();
            if (_followControllerInstalled && _soundBearingFirst is not null)
            {
                await SyncSoundFinderMapAsync();
            }
        }
    }

    private async Task<bool> ExecuteMapperCommandAsync(string expression)
    {
        await EnsureFollowControllerAsync();
        if (LiveMapWebView.CoreWebView2 is null || !_followControllerInstalled)
        {
            return false;
        }

        try
        {
            var result = await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(expression);
            return !string.Equals(result, "false", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(result, "null", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<T?> ExecuteMapperJsonAsync<T>(string expression)
    {
        await EnsureFollowControllerAsync();
        if (LiveMapWebView.CoreWebView2 is null || !_followControllerInstalled)
        {
            return default;
        }

        try
        {
            var result = await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                $"JSON.stringify({expression} ?? null)");
            var json = JsonSerializer.Deserialize<string>(result);
            return string.IsNullOrWhiteSpace(json)
                ? default
                : JsonSerializer.Deserialize<T>(json, MapperJsonOptions);
        }
        catch
        {
            return default;
        }
    }
}
