using System.Text.Json;
using Luma.Models;
using Microsoft.UI.Xaml.Controls;

namespace Luma.Services;

public sealed class SnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<BrowserSnapshotItem>> CaptureAsync(WebView2 webView)
    {
        if (webView.CoreWebView2 is null)
        {
            return [];
        }

        var json = await webView.CoreWebView2.ExecuteScriptAsync(BrowserScripts.SnapshotScript);
        return JsonSerializer.Deserialize<List<BrowserSnapshotItem>>(json, JsonOptions) ?? [];
    }
}
