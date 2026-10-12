using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Luma.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Luma.Services;

public sealed class VaultAutofillService(SecureVaultService vault)
{
    private bool _filling;
    public bool IsAutofilling => _filling;

    // Invoke only from native Fill UI after approval; never from a page web message.
    // The caller owns recording suppression and selected-tab/activation invalidation.
    public async Task<VaultFillResult> FillAsync(WebView2 webView, Guid id, nint windowHandle, bool userInitiated,
        Func<bool> isForegroundAndActiveTab, Func<IDisposable> suspendRecording,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webView);
        ArgumentNullException.ThrowIfNull(isForegroundAndActiveTab);
        ArgumentNullException.ThrowIfNull(suspendRecording);
        if (!userInitiated) return Refuse("user-gesture-required");
        if (!webView.DispatcherQueue.HasThreadAccess) return Refuse("ui-thread-required");
        var core = webView.CoreWebView2;
        if (_filling || core is null) return Refuse("unavailable");
        bool Active() => windowHandle != 0 && GetForegroundWindow() == windowHandle &&
            isForegroundAndActiveTab() && webView.IsLoaded && webView.Visibility == Visibility.Visible &&
            ReferenceEquals(webView.CoreWebView2, core);
        if (!Active()) return Refuse("inactive-window-or-tab");
        string origin;
        try { origin = SecureVaultService.NormalizeOrigin(core.Source); }
        catch (ArgumentException) { return Refuse("https-required"); }
        var href = core.Source;
        var documentKey = "__lumaVault_" + Guid.NewGuid().ToString("N");
        var documentToken = Guid.NewGuid().ToString("N");
        bool invalidated = false;
        void Navigating(object? sender, CoreWebView2NavigationStartingEventArgs args) => invalidated = true;
        core.NavigationStarting += Navigating;
        _filling = true;
        try
        {
            // No secret is resolved until the caller has suspended recording.
            using var suppression = suspendRecording() ?? throw new InvalidOperationException("Recording suppression is required.");
            cancellationToken.ThrowIfCancellationRequested();
            webView.Focus(FocusState.Programmatic);
            var preflight = $$"""
(() => {
  if (window !== window.top || location.protocol !== 'https:' || location.origin !== {{JsonSerializer.Serialize(origin)}} ||
      location.href !== {{JsonSerializer.Serialize(href)}} || document.visibilityState !== 'visible' || !document.hasFocus()) return false;
  Object.defineProperty(window, {{JsonSerializer.Serialize(documentKey)}}, {value:{{JsonSerializer.Serialize(documentToken)}}, configurable:true});
  return true;
})()
""";
            if (await core.ExecuteScriptAsync(preflight) != "true") return Refuse("document-check-failed");
            return await vault.UseEntryAsync(id, async (entry, password) =>
            {
                if (!SecureVaultService.IsExactOrigin(entry.Origin, origin)) return Refuse("origin-mismatch");
                var script = BuildFillScript(entry, password, href, documentKey, documentToken);
                cancellationToken.ThrowIfCancellationRequested();
                // Last native checks immediately before scheduling the single-use injection.
                if (invalidated || !Active() || core.Source != href) return Refuse("document-changed");
                var response = await core.ExecuteScriptAsync(script);
                try
                {
                    var result = JsonSerializer.Deserialize<ScriptResult>(response);
                    return result is { Ok: true, Count: > 0 }
                        ? new VaultFillResult(true, result.Count) : Refuse("no-safe-fields-or-document-changed");
                }
                catch (JsonException) { return Refuse("document-check-failed"); }
            }, cancellationToken);
        }
        finally
        {
            core.NavigationStarting -= Navigating;
            _filling = false;
        }
    }

    public static bool IsAllowedField(VaultEntry entry, string autocomplete)
    {
        // Reject compound section/shipping/billing tokens rather than guessing a section.
        if (string.IsNullOrEmpty(autocomplete) || autocomplete != autocomplete.Trim() || autocomplete.Any(char.IsWhiteSpace)) return false;
        return entry switch
        {
            LoginVaultEntry => autocomplete is "username" or "current-password",
            AddressVaultEntry => autocomplete is "name" or "address-line1" or "address-line2" or "address-level2" or
                "address-level1" or "postal-code" or "country" or "email" or "tel",
            PaymentVaultEntry => autocomplete is "cc-name" or "cc-exp-month" or "cc-exp-year",
            _ => false
        };
    }

    internal static string BuildFillScript(VaultEntry entry, string? password, string href, string documentKey, string documentToken)
    {
        SecureVaultService.ValidateEntry(entry);
        var fields = new Dictionary<string, string>();
        switch (entry)
        {
            case LoginVaultEntry login:
                if (password is null) throw new InvalidOperationException("Login secret is unavailable.");
                fields.Add("username", login.Username); fields.Add("current-password", password);
                break;
            case AddressVaultEntry address:
                fields.Add("name", address.FullName); fields.Add("address-line1", address.AddressLine1);
                fields.Add("address-line2", address.AddressLine2); fields.Add("address-level2", address.City);
                fields.Add("address-level1", address.Region); fields.Add("postal-code", address.PostalCode);
                fields.Add("country", address.CountryCode); fields.Add("email", address.Email); fields.Add("tel", address.Phone);
                break;
            case PaymentVaultEntry payment:
                fields.Add("cc-name", payment.Holder);
                fields.Add("cc-exp-month", payment.ExpiryMonth.ToString("00", CultureInfo.InvariantCulture));
                fields.Add("cc-exp-year", payment.ExpiryYear.ToString(CultureInfo.InvariantCulture));
                break;
        }
        // Default JSON encoder escapes script-breaking characters; never use UnsafeRelaxedJsonEscaping.
        return $$"""
(() => {
  'use strict';
  const origin = {{JsonSerializer.Serialize(SecureVaultService.NormalizeOrigin(entry.Origin))}};
  const href = {{JsonSerializer.Serialize(href)}};
  const key = {{JsonSerializer.Serialize(documentKey)}}, token = {{JsonSerializer.Serialize(documentToken)}};
  const valid = () => window === window.top && location.protocol === 'https:' && location.origin === origin &&
      location.href === href && window[key] === token && document.visibilityState === 'visible' && document.hasFocus();
  if (!valid()) return {ok:false,count:0};
  delete window[key]; // consume the authorization once, even on an ambiguous/unsafe form
  const fields = {{JsonSerializer.Serialize(fields)}};
  const login = {{(entry is LoginVaultEntry ? "true" : "false")}};
  const blocked = /(?:one.?time|otp|totp|cvv|cvc|csc|security.?code|verification.?code|card.?number|credit.?card.?number|\bpan\b|\bpin\b|new.?password)/i;
  const all = [...document.querySelectorAll('input[autocomplete],textarea[autocomplete],select[autocomplete]')];
  const plans = [];
  const visible = el => {
    const rect = el.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0 || rect.bottom <= 0 || rect.right <= 0 ||
        rect.top >= window.innerHeight || rect.left >= window.innerWidth) return false;
    for (let ancestor = el; ancestor; ancestor = ancestor.parentElement) {
      const style = getComputedStyle(ancestor);
      if (style.visibility !== 'visible' || style.display === 'none' || Number(style.opacity) === 0) return false;
    }
    return true;
  };
  for (const [field,value] of Object.entries(fields)) {
    if (!value) continue;
    const matches = all.filter(el => el.getAttribute('autocomplete') === field);
    if (matches.length > 1) return {ok:false,count:0};
    if (matches.length === 0) continue;
    const el = matches[0];
    const hints = [el.name,el.id,el.getAttribute('aria-label'),el.getAttribute('placeholder'),
      ...[...(el.labels || [])].map(label => label.textContent)].join(' ');
    const type = el instanceof HTMLInputElement ? el.type : '';
    if (blocked.test(hints) || el.matches(':disabled') || el.readOnly || el.closest('[inert],[hidden],[aria-hidden="true"]') ||
        el.getAttribute('aria-disabled') === 'true' || !visible(el) ||
        (el instanceof HTMLInputElement && !['text','email','tel','password','number'].includes(type)) ||
        (field === 'current-password' && (!(el instanceof HTMLInputElement) || type !== 'password')) ||
        (field !== 'current-password' && type === 'password') ||
        (login && !(el instanceof HTMLInputElement))) return {ok:false,count:0};
    if (el.form) {
      const action = new URL(el.form.action || location.href, location.href);
      if (action.protocol !== 'https:' || action.origin !== origin || el.form.method.toLowerCase() !== 'post')
        return {ok:false,count:0};
    } else if (login) return {ok:false,count:0};
    let chosen = value;
    if (el instanceof HTMLSelectElement) {
      const choices = [...el.options].filter(option => !option.matches(':disabled') && option.value === value);
      if (choices.length !== 1) return {ok:false,count:0};
      chosen = choices[0].value;
    }
    const proto = el instanceof HTMLInputElement ? HTMLInputElement.prototype :
      el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLSelectElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(proto,'value')?.set;
    if (!setter) return {ok:false,count:0};
    plans.push({el,chosen,setter});
  }
  // Recheck the document immediately before the first write. No awaits or frame traversal.
  if (window !== window.top || location.origin !== origin || location.href !== href ||
      document.visibilityState !== 'visible' || !document.hasFocus()) return {ok:false,count:0};
  for (const {el,chosen,setter} of plans) setter.call(el,chosen);
  // Deliberately no input/change events, clicks, focus, keypress, or submit. Recording sees no autofill events.
  return {ok:plans.length > 0,count:plans.length};
})()
""";
    }
    private static VaultFillResult Refuse(string reason) => new(false, 0, reason);
    private sealed class ScriptResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("ok")] public bool Ok { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("count")] public int Count { get; set; }
    }
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();
}
