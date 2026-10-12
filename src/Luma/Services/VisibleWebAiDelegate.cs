using System.Text.Json;
using Microsoft.UI.Xaml.Controls;

namespace Luma.Services;

/// <summary>Visible composer handoff. User sends the prompt and selects the reply; no network API.</summary>
public sealed class VisibleWebAiDelegate : IExternalAiDelegate
{
    private readonly Func<string, CancellationToken, Task<WebView2>> _openTab;
    private readonly Func<WebView2, bool> _canExecute;
    private WebView2? _tab;
    private string? _prompt;
    public string Name { get; }
    public string Origin { get; }
    public bool IsAvailable => _tab?.CoreWebView2 is not null;

    public VisibleWebAiDelegate(string name, string origin,
        Func<string, CancellationToken, Task<WebView2>> openTab, Func<WebView2, bool> canExecute)
    {
        Name = name; Origin = origin; _openTab = openTab; _canExecute = canExecute;
    }

    public async Task<string> DelegateVisibleAsync(string task, string? context, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(context)) throw new ArgumentException("Automatic page context sharing is disabled.");
        _prompt = DelegatePrivacy.PreparePrompt(task);
        _tab = await _openTab(Origin, cancellationToken);
        return _prompt;
    }

    public async Task<bool> FillComposerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureVisibleOrigin();
        var script = $$"""
        (() => {
          if (location.origin !== {{JsonSerializer.Serialize(Origin)}} || !document.hasFocus()) return false;
          const visible = el => el.getBoundingClientRect().width > 0 && el.getBoundingClientRect().height > 0 &&
            getComputedStyle(el).visibility === 'visible' && !el.disabled && !el.readOnly;
          const editors = [...document.querySelectorAll('textarea,[contenteditable="true"][role="textbox"],.ProseMirror[contenteditable="true"]')].filter(visible);
          if (editors.length !== 1) return false;
          const el = editors[0];
          if ((el.value || el.textContent || '').trim()) return false;
          el.focus();
          if (el.tagName === 'TEXTAREA') {
            Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(el, {{JsonSerializer.Serialize(_prompt)}});
          } else el.textContent = {{JsonSerializer.Serialize(_prompt)}};
          el.dispatchEvent(new Event('input', {bubbles:true}));
          return true;
        })()
        """;
        return await _tab!.CoreWebView2.ExecuteScriptAsync(script) == "true";
    }

    public async Task<string> ReadSelectedReplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureVisibleOrigin();
        var result = await _tab!.CoreWebView2.ExecuteScriptAsync($$"""
          (() => location.origin === {{JsonSerializer.Serialize(Origin)}} ? (window.getSelection()?.toString() || '').slice(0,16000) : '')()
          """);
        EnsureVisibleOrigin();
        return JsonSerializer.Deserialize<string>(result) ?? string.Empty;
    }

    private void EnsureVisibleOrigin()
    {
        if (_tab?.CoreWebView2 is null || !_canExecute(_tab) ||
            !Uri.TryCreate(_tab.CoreWebView2.Source, UriKind.Absolute, out var uri) ||
            uri.GetLeftPart(UriPartial.Authority) != Origin)
            throw new InvalidOperationException("Select the visible delegate tab in active Luma; complete login manually first.");
    }
}
