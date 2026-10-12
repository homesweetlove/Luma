using System.Text.Json;
using Luma.Models;
using Microsoft.UI.Xaml.Controls;

namespace Luma.Services;

public sealed class ForegroundWatchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Both observation and effect use the same eligibility and exact-text rules.
    private const string MatchingScript = """
  const normalize = value => String(value ?? '').replace(/\s+/gu, ' ').trim();
  const labelFor = el => normalize(el instanceof HTMLInputElement
    ? el.value || el.getAttribute('aria-label')
    : el.innerText || el.getAttribute('aria-label'));
  const eligible = el => {
    if (!(el instanceof HTMLElement) || !el.isConnected || typeof el.click !== 'function') return false;
    if (el.matches(':disabled') || el.closest('[inert]')) return false;
    for (let node = el; node; node = node.parentElement) {
      if (normalize(node.getAttribute('aria-disabled')).toLowerCase() === 'true') return false;
      const style = getComputedStyle(node);
      if (style.display === 'none' || style.visibility === 'hidden' || style.visibility === 'collapse'
          || Number(style.opacity) === 0 || style.pointerEvents === 'none') return false;
    }
    // Require an unobstructed point in the viewport; observing never scrolls the page.
    return [...el.getClientRects()].some(rect => {
      const left = Math.max(0, rect.left), top = Math.max(0, rect.top);
      const right = Math.min(innerWidth, rect.right), bottom = Math.min(innerHeight, rect.bottom);
      if (right <= left || bottom <= top) return false;
      const hit = document.elementFromPoint((left + right) / 2, (top + bottom) / 2);
      return hit === el || (hit !== null && el.contains(hit));
    });
  };
  const candidates = () => [...document.querySelectorAll(
    'button,a[href],[role="button"],[role="link"],input[type="button"],input[type="submit"],input[type="reset"]'
  )].filter(el => eligible(el) && labelFor(el) === needle);
  const uniqueSelector = el => {
    if (el.id) {
      const selector = '#' + CSS.escape(el.id);
      const matches = document.querySelectorAll(selector);
      if (matches.length === 1 && matches[0] === el) return selector;
    }
    const parts = [];
    for (let node = el; node; node = node.parentElement) {
      let index = 1;
      for (let sibling = node.previousElementSibling; sibling; sibling = sibling.previousElementSibling) index++;
      parts.unshift(CSS.escape(node.localName) + ':nth-child(' + index + ')');
    }
    const selector = parts.join(' > ');
    const matches = document.querySelectorAll(selector);
    return matches.length === 1 && matches[0] === el ? selector : null;
  };
  const failure = (error, matchCount = 0) => ({
    found: false, clicked: false, matchedText: null, selector: null, error, matchCount
  });
""";

    public Task<WatchResult> CheckAsync(WebView2 webView, string text, bool autoClick)
    {
        if (autoClick)
        {
            throw new InvalidOperationException("Automatic clicking requires ClickAsync with an explicit caller gate.");
        }

        return ObserveAsync(webView, text);
    }

    public Task<WatchResult> CheckAsync(
        WebView2 webView, string text, bool autoClick,
        Func<CancellationToken, Task<bool>> executionGate,
        CancellationToken cancellationToken = default, string? approvedSelector = null,
        string? approvedSource = null, string? approvedDocumentId = null)
    {
        ArgumentNullException.ThrowIfNull(executionGate);
        return autoClick
            ? ClickAsync(webView, text, executionGate, cancellationToken, approvedSelector, approvedSource, approvedDocumentId)
            : ObserveAsync(webView, text, cancellationToken);
    }

    public async Task<WatchResult> ObserveAsync(
        WebView2 webView, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webView);
        cancellationToken.ThrowIfCancellationRequested();
        if (webView.CoreWebView2 is null || string.IsNullOrWhiteSpace(text))
        {
            return Failure("A ready browser and nonempty target text are required.");
        }

        var script = $$"""
(() => {
  const needle = String({{JsonSerializer.Serialize(text)}}).replace(/\s+/gu, ' ').trim();
{{MatchingScript}}
  if (!needle) return failure('Target text is empty.');
  const matches = candidates();
  if (matches.length !== 1) return failure(matches.length ? 'Target text is ambiguous.' : null, matches.length);
  const el = matches[0];
  const selector = uniqueSelector(el);
  if (!selector) return failure('A unique target selector could not be verified.', 1);
  return {found: true, clicked: false, matchedText: labelFor(el), selector, error: null, matchCount: 1,
    source:location.href, documentId:String(performance.timeOrigin)};
})()
""";

        var result = await ExecuteAsync(webView, script, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<WatchResult> ClickAsync(
        WebView2 webView, string text, Func<CancellationToken, Task<bool>> callerGate,
        CancellationToken cancellationToken = default, string? approvedSelector = null,
        string? approvedSource = null, string? approvedDocumentId = null)
    {
        ArgumentNullException.ThrowIfNull(webView);
        ArgumentNullException.ThrowIfNull(callerGate);
        cancellationToken.ThrowIfCancellationRequested();
        // An approval must be bound to the original observation, never a fresh target/page.
        if (string.IsNullOrWhiteSpace(approvedSelector) || string.IsNullOrWhiteSpace(approvedSource) ||
            string.IsNullOrWhiteSpace(approvedDocumentId))
        {
            return Failure("Clicking requires the approved selector, source and document ID from observation.");
        }
        if (!await callerGate(cancellationToken).WaitAsync(cancellationToken))
        {
            return Failure("Caller gate denied clicking.");
        }

        var core = webView.CoreWebView2;
        var source = core?.Source;
        var observed = await ObserveAsync(webView, text, cancellationToken);
        if (!observed.Found || observed.MatchCount != 1 || observed.Selector is null)
        {
            return observed;
        }

        if (!string.Equals(observed.Selector, approvedSelector, StringComparison.Ordinal))
        {
            return observed with { Error = "Target selector changed since caller approval." };
        }
        if (!string.Equals(observed.Source, approvedSource, StringComparison.Ordinal) ||
            !string.Equals(observed.DocumentId, approvedDocumentId, StringComparison.Ordinal))
        {
            return observed with { Error = "Document changed since caller approval." };
        }

        // Construct the effect before the final gate; never click a saved DOM element.
        var script = $$"""
(() => {
  const needle = String({{JsonSerializer.Serialize(text)}}).replace(/\s+/gu, ' ').trim();
  const expectedSelector = {{JsonSerializer.Serialize(observed.Selector)}};
  const expectedSource = {{JsonSerializer.Serialize(observed.Source)}};
  const expectedDocument = {{JsonSerializer.Serialize(observed.DocumentId)}};
{{MatchingScript}}
  // Native Source checks cannot cover navigation after dispatch. Validate in this document.
  if (!expectedSelector || !expectedSource || !expectedDocument ||
      location.href !== expectedSource || String(performance.timeOrigin) !== expectedDocument)
    return failure('Document changed since caller approval.');
  if (!needle) return failure('Target text is empty.');
  const matches = candidates();
  if (matches.length !== 1) return failure(matches.length ? 'Target text is ambiguous.' : 'Target is no longer available.', matches.length);
  const el = matches[0];
  const selected = document.querySelectorAll(expectedSelector);
  if (selected.length !== 1 || selected[0] !== el || uniqueSelector(el) !== expectedSelector)
    return failure('Target changed since observation.', matches.length);
  const matchedText = labelFor(el);
  el.click();
  return {found: true, clicked: true, matchedText, selector: expectedSelector, error: null, matchCount: 1,
    source:expectedSource, documentId:expectedDocument};
})()
""";

        if (!await callerGate(cancellationToken).WaitAsync(cancellationToken))
        {
            return observed with { Error = "Caller gate denied clicking." };
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (core is null || !ReferenceEquals(core, webView.CoreWebView2) || core.Source != source)
        {
            return Failure("Browser changed since observation.");
        }

        // WebView2 cannot cancel a script after dispatch. Do not retry or hide its effect result.
        return await ExecuteAsync(webView, script);
    }

    private static async Task<WatchResult> ExecuteAsync(
        WebView2 webView, string script, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var core = webView.CoreWebView2;
            if (core is null) return Failure("Browser is not ready.");
            var json = await core.ExecuteScriptAsync(script).AsTask().WaitAsync(cancellationToken);
            return JsonSerializer.Deserialize<WatchResult>(json, JsonOptions)
                ?? Failure("Browser returned no watch result.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure("Browser script failed: " + exception.Message);
        }
    }

    private static WatchResult Failure(string error) => new(false, false, null, null)
    {
        Error = error,
        MatchCount = 0
    };
}
