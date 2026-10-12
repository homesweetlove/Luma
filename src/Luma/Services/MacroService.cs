using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Luma.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Luma.Services;

public sealed class MacroService
{
    private readonly List<MacroStep> _steps = [];
    private int _replaying;
    private MacroRuntime? _activeRuntime;
    public bool IsRecording { get; private set; }
    public bool IsReplaying => Volatile.Read(ref _replaying) != 0 || _activeRuntime?.IsRunning == true;
    public IReadOnlyList<MacroStep> Steps => _steps.AsReadOnly();

    // Configure these from MainWindow; a missing gate/approval always fails closed.
    public Func<WebView2, CancellationToken, Task<bool>>? ExecutionGateAsync { get; set; }
    public Func<MacroStep, CancellationToken, Task<bool>>? ApproveClickAsync { get; set; }
    public Func<MacroStep, MacroDomState, CancellationToken, Task<string?>>? SelfHealAsync { get; set; }
    // Bridge messages are untrusted. Input recording is disabled until the host supplies
    // an allowlist validator for known nonsensitive fields; page metadata alone is insufficient.
    public Func<string, string, bool>? InputCaptureValidator { get; set; }

    private const int MaxJsonBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions MacroJsonOptions = new()
    {
        MaxDepth = 64, PropertyNameCaseInsensitive = true, WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public void ReplaceSteps(IEnumerable<MacroStep> steps)
    {
        if (IsReplaying || IsRecording) throw new InvalidOperationException("Stop recording/replay before replacing steps.");
        var validated = new MacroRuntime(new ValidationHost()).ValidateSteps(steps);
        _steps.Clear();
        _steps.AddRange(validated);
    }

    public void LoadSteps(IEnumerable<MacroStep> steps) => ReplaceSteps(steps);

    public void ImportJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            throw new MacroExecutionException("import-too-large", "Macro JSON is limited to 1 MiB.");
        List<MacroStep>? parsed;
        try { parsed = JsonSerializer.Deserialize<List<MacroStep>>(json, MacroJsonOptions); }
        catch (JsonException exception)
        { throw new MacroExecutionException("invalid-json", "Malformed macro JSON: " + exception.Message); }
        ReplaceSteps(parsed ?? throw new MacroExecutionException("invalid-program", "Macro JSON must be an array."));
    }

    public string ExportJson()
    {
        var validated = new MacroRuntime(new ValidationHost()).ValidateSteps(_steps);
        var json = JsonSerializer.Serialize(validated, MacroJsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            throw new MacroExecutionException("export-too-large", "Macro JSON is limited to 1 MiB.");
        return json;
    }

    public async Task SaveAsync(string dataRoot, string fileName = "macros.json", CancellationToken cancellationToken = default)
    {
        var path = MacroPath(dataRoot, fileName);
        var json = ExportJson();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task LoadAsync(string dataRoot, string fileName = "macros.json", CancellationToken cancellationToken = default)
    {
        var path = MacroPath(dataRoot, fileName);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxJsonBytes) throw new MacroExecutionException("import-too-large", "Macro JSON is limited to 1 MiB.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var json = await reader.ReadToEndAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ImportJson(json);
    }

    private static string MacroPath(string dataRoot, string fileName)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(fileName) ||
            Path.GetFileName(fileName) != fileName || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a data root and a simple .json filename.");
        var root = Path.GetFullPath(dataRoot);
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path)!), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The macro file must remain inside the data root.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Macro files cannot be links.");
        return path;
    }

    private sealed class ValidationHost : IMacroHost
    {
        public Task<bool> CheckGateAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<MacroDomState> ObserveAsync(string selector, CancellationToken token) => throw new NotSupportedException();
        public Task ExecuteAsync(MacroStep step, CancellationToken token) => throw new NotSupportedException();
    }

    public void Start(bool clearExisting = true)
    {
        if (IsReplaying) throw new InvalidOperationException("Cannot record during replay.");
        if (clearExisting) _steps.Clear();
        IsRecording = true;
    }
    public void Stop() => IsRecording = false;

    public void CaptureNavigation(string url)
    {
        if (!IsRecording || IsReplaying || string.IsNullOrWhiteSpace(url) || _steps.LastOrDefault()?.Url == url) return;
        _steps.Add(new MacroStep { Kind = "navigate", Url = url });
    }

    public bool TryCaptureWebMessage(string json)
    {
        if (!IsRecording || IsReplaying || string.IsNullOrWhiteSpace(json) || json.Length > 16384) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryString(root, "source", out var source) || source != "luma-macro" ||
                !TryString(root, "kind", out var kind) || kind is not ("click" or "input") ||
                !TryString(root, "selector", out var selector) || string.IsNullOrWhiteSpace(selector)) return false;
            if (root.TryGetProperty("sensitive", out var sensitive) && sensitive.ValueKind == JsonValueKind.True) return false;
            if (selector!.Length > 2048 || selector.Any(char.IsControl)) return false;
            string? value = null;
            if (kind == "input")
            {
                if (!TryString(root, "value", out value) || value is null || value.Length > 4096 ||
                    value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) ||
                    Regex.IsMatch(selector, @"password|passwd|passcode|secret|token|api.?key|credit|card|cvv|cvc|ssn|otp|pin\b|비밀번호|인증번호", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(value, @"(?:sk-|gh[pousr]_|github_pat_|AKIA)[A-Za-z0-9_-]{8,}|\b\d{3}-\d{2}-\d{4}\b|^\s*\d[\d -]{5,23}\s*$", RegexOptions.IgnoreCase) ||
                    InputCaptureValidator is not { } validator || !validator(selector, value)) return false;
            }
            _steps.Add(new MacroStep { Kind = kind!, Selector = selector, Value = value });
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString();
        return true;
    }

    public Task ReplayAsync(WebView2 webView, CancellationToken cancellationToken = default)
    {
        if (webView.CoreWebView2 is null) throw new InvalidOperationException("The browser is not ready.");
        var gate = ExecutionGateAsync ?? throw new InvalidOperationException("Configure ExecutionGateAsync before replay.");
        return ReplayAsync(new WebViewMacroHost(webView, token => gate(webView, token)), cancellationToken);
    }

    public async Task ReplayAsync(IMacroHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (IsReplaying || Interlocked.CompareExchange(ref _replaying, 1, 0) != 0)
            throw new MacroExecutionException("busy", "A macro is already running.");
        var wasRecording = IsRecording;
        IsRecording = false;
        try
        {
            var runtime = new MacroRuntime(host, new MacroRuntimeOptions
            {
                ApproveClickAsync = ApproveClickAsync,
                SelfHealAsync = SelfHealAsync
            });
            _activeRuntime = runtime;
            await runtime.ReplayAsync(_steps.ToArray(), cancellationToken);
        }
        finally
        {
            IsRecording = wasRecording;
            Volatile.Write(ref _replaying, 0);
        }
    }
}

/// <summary>Use on the WebView UI thread. The caller gate must check both the actual
/// foreground window and whether this exact WebView is still the selected tab.</summary>
public sealed class WebViewMacroHost(WebView2 webView, Func<CancellationToken, Task<bool>> gate) : IMacroHost
{
    private CoreWebView2 Core => webView.CoreWebView2 ?? throw new InvalidOperationException("The browser was closed.");

    public async Task<bool> CheckGateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allowed = await gate(cancellationToken).WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return allowed;
    }

    private async Task RequireGateAsync(CancellationToken token)
    {
        if (!await CheckGateAsync(token))
            throw new MacroExecutionException("gate-denied", "The browser is no longer the selected foreground tab.");
        token.ThrowIfCancellationRequested();
    }

    // Shared by observation and effect scripts, so effect-time checks use the same rules.
    private const string DomChecks = """
const visible = el => {
  if (!el.isConnected || el.closest('[hidden],[inert],[aria-hidden="true"]')) return false;
  for (let p = el; p; p = p.parentElement) {
    const s = getComputedStyle(p);
    if (s.display === 'none' || s.visibility === 'hidden' || s.visibility === 'collapse' || Number(s.opacity) === 0) return false;
  }
  const r = el.getBoundingClientRect();
  return r.width > 0 && r.height > 0;
};
const enabled = el => !el.disabled && !el.matches(':disabled') && !el.closest('[aria-disabled="true"],[inert]') && !el.readOnly;
const sensitive = el => {
  if (!el.matches('input,textarea,select,[contenteditable]')) return false;
  const type = (el.getAttribute('type') || '').toLowerCase();
  const autocomplete = (el.getAttribute('autocomplete') || '').toLowerCase();
  const metadata = [el.id, el.name, el.getAttribute('aria-label'), el.getAttribute('placeholder'),
    ...Array.from(el.labels || []).map(label => label.innerText)].filter(Boolean).join(' ');
  return ['password','hidden','file'].includes(type) ||
    /(?:password|one-time-code|cc-|transaction-|webauthn)/i.test(autocomplete) ||
    /(?:password|passwd|passcode|pin\b|otp\b|secret|token|api[ _-]?key|credit|card|cvv|cvc|security.code|ssn|social.security|account.number|routing.number|비밀번호|카드번호|인증번호)/i.test(metadata);
};
""";

    public async Task<MacroDomState> ObserveAsync(string selector, CancellationToken cancellationToken)
    {
        var script = $$"""
(() => {
  {{DomChecks}}
  try {
    const matches = [...document.querySelectorAll({{JsonSerializer.Serialize(selector)}})];
    const el = matches.length === 1 ? matches[0] : null;
    return {matchCount:matches.length, visible:!!el && visible(el), enabled:!!el && enabled(el), sensitive:!!el && sensitive(el),
      source:location.href, documentId:String(performance.timeOrigin)};
  } catch (_) { return {matchCount:0, error:'Invalid DOM selector.'}; }
})()
""";
        await RequireGateAsync(cancellationToken);
        var json = await Core.ExecuteScriptAsync(script).AsTask().WaitAsync(cancellationToken);
        return JsonSerializer.Deserialize<MacroDomState>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new MacroExecutionException("dom-error", "No DOM observation result.");
    }

    public async Task ExecuteAsync(MacroStep step, CancellationToken cancellationToken)
    {
        if (step.Kind == "navigate")
        {
            await NavigateAsync(step.Url!, cancellationToken);
            return;
        }
        if (step.Kind is not ("click" or "input")) throw new MacroExecutionException("invalid-kind", "Unsupported effect.");
        var script = $$"""
(() => {
  {{DomChecks}}
  const expectedSource = {{JsonSerializer.Serialize(step.ExpectedSource)}};
  const expectedDocument = {{JsonSerializer.Serialize(step.ExpectedDocumentId)}};
  if (!expectedSource || !expectedDocument || location.href !== expectedSource || String(performance.timeOrigin) !== expectedDocument)
    return 'document-changed';
  const matches = [...document.querySelectorAll({{JsonSerializer.Serialize(step.Selector)}})];
  if (matches.length !== 1) return 'ambiguous-or-missing';
  const el = matches[0];
  if (!visible(el) || !enabled(el)) return 'not-ready';
  if (sensitive(el)) return 'sensitive-input';
  if ({{JsonSerializer.Serialize(step.Kind)}} === 'click') {
    if (typeof el.click !== 'function') return 'unsupported-target';
    el.click();
  } else {
    if (!el.matches('input,textarea') || (el instanceof HTMLInputElement &&
        !['text','search','email','url','tel','number'].includes(el.type))) return 'unsupported-input';
    const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
    if (!setter) return 'unsupported-input';
    setter.call(el, {{JsonSerializer.Serialize(step.Value ?? string.Empty)}});
    el.dispatchEvent(new Event('input', {bubbles:true}));
    el.dispatchEvent(new Event('change', {bubbles:true}));
  }
  return 'ok';
})()
""";
        await RequireGateAsync(cancellationToken);
        // Preserve the actual effect task so runtime single flight survives a timeout.
        var json = await Core.ExecuteScriptAsync(script).AsTask();
        var result = JsonSerializer.Deserialize<string>(json);
        if (result != "ok") throw new MacroExecutionException(result ?? "effect-failed", "The DOM effect was rejected.");
    }

    private async Task NavigateAsync(string url, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new MacroExecutionException("invalid-url", "Navigation requires an absolute HTTP(S) URL.");
        var core = Core;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong? navigationId = null;
        var dispatched = false;
        void Starting(object? sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (!dispatched) return;
            if (navigationId is null) navigationId = args.NavigationId;
            else if (navigationId != args.NavigationId)
                completion.TrySetException(new MacroExecutionException("navigation-superseded", "Another navigation replaced the macro navigation."));
        }
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (navigationId != args.NavigationId) return;
            if (args.IsSuccess) completion.TrySetResult();
            else completion.TrySetException(new MacroExecutionException("navigation-failed", "Navigation failed: " + args.WebErrorStatus));
        }
        await RequireGateAsync(token);
        if (!ReferenceEquals(core, webView.CoreWebView2))
            throw new MacroExecutionException("browser-changed", "The browser changed during the navigation gate check.");
        core.NavigationStarting += Starting;
        core.NavigationCompleted += Completed;
        try
        {
            token.ThrowIfCancellationRequested();
            dispatched = true;
            core.Navigate(url);
            await completion.Task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancellation resumes on the WebView UI thread. Stop only our still-active
            // navigation; a newer NavigationStarting has already completed this task as
            // superseded, so user navigation is never stopped by macro cleanup.
            if (!completion.Task.IsCompleted) core.Stop();
            throw;
        }
        finally
        {
            core.NavigationStarting -= Starting;
            core.NavigationCompleted -= Completed;
        }
    }
}
