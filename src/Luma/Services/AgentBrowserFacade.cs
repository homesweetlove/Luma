using System.Text.RegularExpressions;
using Luma.Models;
using Microsoft.UI.Xaml.Controls;

namespace Luma.Services;

/// <summary>
/// A small, transport-agnostic browser surface for future Codex/Claude/MCP adapters.
/// It deliberately reuses MacroRuntime so external callers cannot bypass the same
/// foreground gate, DOM validation, operation budgets, cancellation, or click approval.
/// This class does not open a socket or expose an unattended remote-control endpoint.
/// </summary>
public sealed class AgentBrowserFacade
{
    private readonly SnapshotService _snapshotService;
    private readonly Func<WebView2?> _activeView;
    private readonly Func<WebView2, bool> _executionGate;
    private readonly Func<MacroStep, CancellationToken, Task<bool>> _approveClickAsync;

    public AgentBrowserFacade(
        SnapshotService snapshotService,
        Func<WebView2?> activeView,
        Func<WebView2, bool> executionGate,
        Func<MacroStep, CancellationToken, Task<bool>> approveClickAsync)
    {
        _snapshotService = snapshotService;
        _activeView = activeView;
        _executionGate = executionGate;
        _approveClickAsync = approveClickAsync;
    }

    public async Task<IReadOnlyList<BrowserSnapshotItem>> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        var view = RequireActiveView();
        RequireGate(view, cancellationToken);
        var result = await _snapshotService.CaptureAsync(view);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public Task NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Agent navigation requires an absolute HTTP(S) URL without embedded credentials.", nameof(url));
        return ExecuteBatchAsync([new MacroStep { Kind = "navigate", Url = uri.ToString() }], cancellationToken);
    }

    public Task ClickAsync(string selector, CancellationToken cancellationToken = default) =>
        ExecuteBatchAsync([new MacroStep { Kind = "click", Selector = selector }], cancellationToken);

    public Task FillAsync(string selector, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 ||
            Regex.IsMatch(value, @"(?:sk-|gh[pousr]_|github_pat_|AKIA)[A-Za-z0-9_-]{8,}|^\s*\d[\d -]{11,23}\s*$", RegexOptions.IgnoreCase))
            throw new ArgumentException("Agent fill refuses empty, oversized, credential-like, or card-like values.", nameof(value));
        return ExecuteBatchAsync([new MacroStep { Kind = "input", Selector = selector, Value = value }], cancellationToken);
    }

    public Task WaitAsync(string selector, string condition = "ready", int timeoutMs = 5000, CancellationToken cancellationToken = default) =>
        ExecuteBatchAsync([new MacroStep { Kind = "wait", Selector = selector, Condition = condition, TimeoutMs = timeoutMs }], cancellationToken);

    public async Task ExecuteBatchAsync(IEnumerable<MacroStep> steps, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var view = RequireActiveView();
        RequireGate(view, cancellationToken);

        // A separate runner means an agent batch never overwrites the user's recorded macro.
        var runner = new MacroService
        {
            ExecutionGateAsync = (target, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(ReferenceEquals(target, view) && _executionGate(target));
            },
            ApproveClickAsync = _approveClickAsync
        };
        runner.ReplaceSteps(steps);
        await runner.ReplayAsync(view, cancellationToken);
    }

    private WebView2 RequireActiveView() =>
        _activeView() ?? throw new InvalidOperationException("No active browser tab is available.");

    private void RequireGate(WebView2 view, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_executionGate(view))
            throw new InvalidOperationException("Agent browser actions require the selected foreground tab.");
    }
}
