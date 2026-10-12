using System.Text.Json;
using Luma.Models;
using Microsoft.UI.Xaml.Controls;

namespace Luma.Services;

public sealed class MacroService
{
    private readonly List<MacroStep> _steps = [];

    public bool IsRecording { get; private set; }
    public IReadOnlyList<MacroStep> Steps => _steps;

    public void Start(bool clearExisting = true)
    {
        if (clearExisting)
        {
            _steps.Clear();
        }

        IsRecording = true;
    }

    public void Stop() => IsRecording = false;

    public void CaptureNavigation(string url)
    {
        if (!IsRecording || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (_steps.LastOrDefault()?.Url == url)
        {
            return;
        }

        _steps.Add(new MacroStep
        {
            Kind = "navigate",
            Url = url
        });
    }

    public bool TryCaptureWebMessage(string json)
    {
        if (!IsRecording || string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("source", out var source) || source.GetString() != "luma-macro")
            {
                return false;
            }

            var kind = root.GetProperty("kind").GetString();
            if (kind is not ("click" or "input"))
            {
                return false;
            }

            _steps.Add(new MacroStep
            {
                Kind = kind!,
                Selector = root.TryGetProperty("selector", out var selector) ? selector.GetString() : null,
                Value = root.TryGetProperty("value", out var value) ? value.GetString() : null
            });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task ReplayAsync(WebView2 webView, CancellationToken cancellationToken = default)
    {
        if (webView.CoreWebView2 is null || _steps.Count == 0)
        {
            return;
        }

        var wasRecording = IsRecording;
        IsRecording = false;

        try
        {
            foreach (var step in _steps.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (step.Kind)
                {
                    case "navigate" when !string.IsNullOrWhiteSpace(step.Url):
                        await NavigateAndWaitAsync(webView, step.Url!, cancellationToken);
                        break;

                    case "click" when !string.IsNullOrWhiteSpace(step.Selector):
                        await ClickAsync(webView, step.Selector!);
                        await Task.Delay(250, cancellationToken);
                        break;

                    case "input" when !string.IsNullOrWhiteSpace(step.Selector):
                        await InputAsync(webView, step.Selector!, step.Value ?? string.Empty);
                        await Task.Delay(100, cancellationToken);
                        break;
                }
            }
        }
        finally
        {
            IsRecording = wasRecording;
        }
    }

    private static async Task NavigateAndWaitAsync(WebView2 webView, string url, CancellationToken cancellationToken)
    {
        if (webView.CoreWebView2 is null)
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Completed(WebView2 sender, Microsoft.UI.Xaml.Controls.CoreWebView2NavigationCompletedEventArgs args)
        {
            sender.NavigationCompleted -= Completed;
            tcs.TrySetResult();
        }

        webView.NavigationCompleted += Completed;
        webView.CoreWebView2.Navigate(url);

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        await tcs.Task;
    }

    private static async Task ClickAsync(WebView2 webView, string selector)
    {
        var jsSelector = JsonSerializer.Serialize(selector);
        var script = $"""
(() => {{
  const el = document.querySelector({jsSelector});
  if (!el) return false;
  el.scrollIntoView({{block:'center', inline:'center'}});
  el.click();
  return true;
}})()
""";
        await webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private static async Task InputAsync(WebView2 webView, string selector, string value)
    {
        var jsSelector = JsonSerializer.Serialize(selector);
        var jsValue = JsonSerializer.Serialize(value);
        var script = $"""
(() => {{
  const el = document.querySelector({jsSelector});
  if (!el) return false;
  el.focus();
  el.value = {jsValue};
  el.dispatchEvent(new Event('input', {{bubbles:true}}));
  el.dispatchEvent(new Event('change', {{bubbles:true}}));
  return true;
}})()
""";
        await webView.CoreWebView2.ExecuteScriptAsync(script);
    }
}
