using System.Text.Json;
using Luma.Models;
using Microsoft.UI.Xaml.Controls;

namespace Luma.Services;

public sealed class ForegroundWatchService
{
    public async Task<WatchResult> CheckAsync(WebView2 webView, string text, bool autoClick)
    {
        if (webView.CoreWebView2 is null || string.IsNullOrWhiteSpace(text))
        {
            return new WatchResult(false, false, null, null);
        }

        var needle = JsonSerializer.Serialize(text.Trim());
        var shouldClick = autoClick ? "true" : "false";
        var script = $"""
(() => {{
  const needle = {needle}.toLocaleLowerCase();
  const candidates = [...document.querySelectorAll('button,a,[role="button"],[role="link"],input[type="button"],input[type="submit"]')];
  const visible = (el) => {{
    const style = getComputedStyle(el);
    const rect = el.getBoundingClientRect();
    return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
  }};
  const selectorFor = (el) => {{
    if (el.id) return '#' + CSS.escape(el.id);
    const aria = el.getAttribute('aria-label');
    if (aria) return `${{el.tagName.toLowerCase()}}[aria-label="${{aria.replace(/"/g, '\\"')}}"]`;
    return el.tagName.toLowerCase();
  }};
  const el = candidates.find((candidate) => {{
    if (!visible(candidate)) return false;
    if (candidate.disabled || candidate.getAttribute('aria-disabled') === 'true') return false;
    const label = (candidate.innerText || candidate.value || candidate.getAttribute('aria-label') || '').trim().toLocaleLowerCase();
    return label.includes(needle);
  }});
  if (!el) return {{found:false, clicked:false, matchedText:null, selector:null}};
  const matchedText = (el.innerText || el.value || el.getAttribute('aria-label') || '').trim().slice(0, 240);
  const selector = selectorFor(el);
  if ({shouldClick}) {{
    el.scrollIntoView({{block:'center', inline:'center'}});
    el.click();
  }}
  return {{found:true, clicked:{shouldClick}, matchedText, selector}};
}})()
""";

        var json = await webView.CoreWebView2.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<WatchResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new WatchResult(false, false, null, null);
    }
}
