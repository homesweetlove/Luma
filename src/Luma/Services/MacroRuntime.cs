using System.Diagnostics;
using Luma.Models;

namespace Luma.Services;

public sealed record MacroDomState(int MatchCount, bool Visible = false, bool Enabled = false,
    bool Sensitive = false, string? Error = null, string? Source = null, string? DocumentId = null)
{
    public bool Ready => MatchCount == 1 && Visible && Enabled && !Sensitive && Error is null;
}

/// <summary>Effects must recheck the foreground/selected-tab gate immediately before dispatch,
/// validate the DOM again, honor cancellation, and never retry an effect internally.</summary>
public interface IMacroHost
{
    Task<bool> CheckGateAsync(CancellationToken cancellationToken);
    Task<MacroDomState> ObserveAsync(string selector, CancellationToken cancellationToken);
    Task ExecuteAsync(MacroStep step, CancellationToken cancellationToken);
}

public sealed class MacroRuntimeOptions
{
    public int MaxDurationMs { get; init; } = 120000;
    public int MaxOperations { get; init; } = 1000;
    public int MaxDepth { get; init; } = 16;
    public Func<MacroStep, CancellationToken, Task<bool>>? ApproveClickAsync { get; init; }
    // Explicit opt-in per step plus a caller-provided hook; no model/provider default.
    public Func<MacroStep, MacroDomState, CancellationToken, Task<string?>>? SelfHealAsync { get; init; }
}

public sealed class MacroExecutionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>DOM-first interpreter. Only pre-effect readiness can be retried. All budgets
/// include observation, approval and self-heal time. Instances reject concurrent replay.</summary>
public sealed class MacroRuntime(IMacroHost host, MacroRuntimeOptions? options = null)
{
    private readonly MacroRuntimeOptions _options = options ?? new();
    private int _running;
    private Task? _pendingEffect;

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public IReadOnlyList<MacroStep> ValidateSteps(IEnumerable<MacroStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return Snapshot(steps, 0, new HashSet<MacroStep>(), new int[1]);
    }

    public async Task ReplayAsync(IEnumerable<MacroStep> steps, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw Fail("busy", "A macro is already running.");
        _pendingEffect = null;
        try
        {
            if (_options.MaxDurationMs is < 1 or > 600000 || _options.MaxOperations is < 1 or > 10000 ||
                _options.MaxDepth is < 1 or > 32)
                throw Fail("invalid-budget", "Invalid macro execution budget.");
            // Validate and detach the complete program before the first effect.
            var program = ValidateSteps(steps);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(_options.MaxDurationMs);
            var execution = new Execution(budget.Token);
            try { await RunAsync(program, execution); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
            { throw Fail("replay-timeout", "The macro exceeded its execution budget."); }
        }
        finally
        {
            if (_pendingEffect is { IsCompleted: false } pending)
            {
                // Cancellation bounds the caller's wait, but cannot recall a dispatched effect.
                // Keep single flight until the host reports its actual completion.
                _ = pending.ContinueWith(task =>
                {
                    _ = task.Exception; // Observe late failures; never retry them.
                    Volatile.Write(ref _running, 0);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else Volatile.Write(ref _running, 0);
        }
    }

    private List<MacroStep> Snapshot(IEnumerable<MacroStep> steps, int depth, HashSet<MacroStep> ancestors, int[] count)
    {
        if (depth >= _options.MaxDepth) throw Fail("depth-limit", "Macro nesting is too deep.");
        var result = new List<MacroStep>();
        foreach (var step in steps)
        {
            if (step is null || !ancestors.Add(step)) throw Fail("invalid-program", "Null or cyclic macro step.");
            if (step.Steps is null || step.ElseSteps is null)
                throw Fail("invalid-program", "Steps and ElseSteps must be arrays, not null.");
            if (++count[0] > _options.MaxOperations) throw Fail("operation-limit", "Macro has too many steps.");
            if (step.Kind is not ("navigate" or "click" or "input" or "wait" or "retry" or "if" or "loop"))
                throw Fail("invalid-kind", "Unsupported macro step: " + step.Kind);
            if (step.TimeoutMs is < 1 or > 30000 || step.PollIntervalMs is < 10 or > 5000 ||
                step.RetryCount is < 0 or > 10 || step.RetryDelayMs is < 0 or > 5000 || step.LoopCount is < 1 or > 100)
                throw Fail("invalid-budget", "Step limits are outside the supported bounds.");
            if ((step.Kind is "click" or "input" or "wait" or "if") && string.IsNullOrWhiteSpace(step.Selector))
                throw Fail("invalid-selector", "This step needs a DOM selector.");
            if (step.Selector?.Length > 2048 || step.Value?.Length > 4096 || step.Url?.Length > 8192)
                throw Fail("invalid-program", "Macro strings exceed their size limits.");
            if (step.Condition is not ("ready" or "exists" or "absent" or "visible" or "enabled"))
                throw Fail("invalid-condition", "Unsupported DOM condition.");
            if (step.Kind == "navigate" && (!Uri.TryCreate(step.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https")))
                throw Fail("invalid-url", "Navigation requires an absolute HTTP(S) URL.");
            if (step.Kind == "retry" && (step.Steps.Count != 1 || step.Steps[0] is null || step.Steps[0].Kind is not ("click" or "input" or "wait")))
                throw Fail("invalid-retry", "Retry requires one DOM action or wait; completed effects cannot be retried.");
            var copy = Copy(step);
            copy.Steps = step.Steps.Count == 0 ? [] : Snapshot(step.Steps, depth + 1, ancestors, count);
            copy.ElseSteps = step.ElseSteps.Count == 0 ? [] : Snapshot(step.ElseSteps, depth + 1, ancestors, count);
            ancestors.Remove(step);
            result.Add(copy);
        }
        return result;
    }

    private static MacroStep Copy(MacroStep s) => new()
    {
        Kind = s.Kind, Selector = s.Selector, Value = s.Value, Url = s.Url,
        TimeoutMs = s.TimeoutMs, PollIntervalMs = s.PollIntervalMs,
        RetryCount = s.RetryCount, RetryDelayMs = s.RetryDelayMs, LoopCount = s.LoopCount,
        Condition = s.Condition, AllowSelfHeal = s.AllowSelfHeal, CreatedAt = s.CreatedAt,
        ExpectedSource = s.ExpectedSource, ExpectedDocumentId = s.ExpectedDocumentId
    };

    private sealed class Execution(CancellationToken token)
    {
        public CancellationToken Token { get; } = token;
        public int Operations { get; set; }
    }

    private void Tick(Execution execution)
    {
        execution.Token.ThrowIfCancellationRequested();
        if (++execution.Operations > _options.MaxOperations)
            throw Fail("operation-limit", "The macro exhausted its operation budget.");
    }

    private async Task GateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await host.CheckGateAsync(token).WaitAsync(token))
            throw Fail("gate-denied", "Automation requires the selected tab in the foreground.");
        token.ThrowIfCancellationRequested();
    }

    private async Task<MacroDomState> ObserveAsync(string selector, Execution execution)
    {
        Tick(execution);
        await GateAsync(execution.Token);
        var state = await host.ObserveAsync(selector, execution.Token).WaitAsync(execution.Token);
        if (state.Error is not null) throw Fail("dom-error", state.Error);
        if (state.MatchCount > 1) throw Fail("ambiguous-target", "The selector matches multiple elements.");
        if (state.Sensitive) throw Fail("sensitive-input", "Sensitive fields cannot be automated.");
        return state;
    }

    private static bool Matches(MacroDomState state, string condition) => condition switch
    {
        "absent" => state.MatchCount == 0,
        "exists" => state.MatchCount == 1,
        "visible" => state.MatchCount == 1 && state.Visible,
        "enabled" => state.MatchCount == 1 && state.Enabled,
        _ => state.Ready
    };

    private async Task RunAsync(IReadOnlyList<MacroStep> steps, Execution execution)
    {
        foreach (var step in steps)
        {
            Tick(execution);
            switch (step.Kind)
            {
                case "loop":
                    for (var i = 0; i < step.LoopCount; i++)
                    { Tick(execution); await RunAsync(step.Steps, execution); }
                    break;
                case "if":
                    var state = await ObserveAsync(step.Selector!, execution);
                    await RunAsync(Matches(state, step.Condition) ? step.Steps : step.ElseSteps, execution);
                    break;
                case "retry":
                    var child = Copy(step.Steps[0]);
                    child.RetryCount = step.RetryCount;
                    child.RetryDelayMs = step.RetryDelayMs;
                    await RunLeafAsync(child, execution);
                    break;
                default:
                    await RunLeafAsync(step, execution);
                    break;
            }
        }
    }

    private async Task RunLeafAsync(MacroStep step, Execution execution)
    {
        step = Copy(step);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(execution.Token);
        limit.CancelAfter(step.TimeoutMs);
        var local = new Execution(limit.Token) { Operations = execution.Operations };
        try
        {
            if (step.Kind != "navigate")
            {
                var state = new MacroDomState(0);
                var ready = false;
                var healed = false;
                // One total timeout for all readiness attempts, not a fresh timeout per retry.
                for (var attempt = 0; attempt <= step.RetryCount && !ready; attempt++)
                {
                    var watch = Stopwatch.StartNew();
                    var attemptMs = Math.Max(1, step.TimeoutMs / (step.RetryCount + 1));
                    do
                    {
                        state = await ObserveAsync(step.Selector!, local);
                        ready = Matches(state, step.Kind == "wait" ? step.Condition : "ready");
                        if (ready) break;
                        if (!healed && step.AllowSelfHeal && _options.SelfHealAsync is { } heal && state.MatchCount == 0)
                        {
                            healed = true;
                            var selector = await heal(Copy(step), state, limit.Token).WaitAsync(limit.Token);
                            if (!string.IsNullOrWhiteSpace(selector)) step.Selector = selector;
                        }
                        if (watch.ElapsedMilliseconds >= attemptMs) break;
                        await Task.Delay(Math.Min(step.PollIntervalMs, Math.Max(1, attemptMs - (int)watch.ElapsedMilliseconds)), limit.Token);
                    } while (watch.ElapsedMilliseconds < attemptMs);
                    if (!ready && attempt < step.RetryCount)
                        await Task.Delay(step.RetryDelayMs, limit.Token);
                }
                if (!ready) throw Fail("dom-timeout", "The DOM condition was not satisfied. Update the selector or explicitly configure a self-heal hook before replaying.");
                if (step.Kind == "wait") return;
                step.ExpectedSource = state.Source;
                step.ExpectedDocumentId = state.DocumentId;
            }
            await GateAsync(limit.Token);
            if (step.Kind == "click")
            {
                if (_options.ApproveClickAsync is not { } approve ||
                    !await approve(Copy(step), limit.Token).WaitAsync(limit.Token))
                    throw Fail("approval-denied", "Macro clicks require explicit caller approval.");
            }
            // Approval/self-heal can yield; revalidate the gate and target after those awaits.
            if (step.Kind is "click" or "input")
            {
                var finalState = await ObserveAsync(step.Selector!, local);
                if (!string.Equals(finalState.Source, step.ExpectedSource, StringComparison.Ordinal) ||
                    !string.Equals(finalState.DocumentId, step.ExpectedDocumentId, StringComparison.Ordinal))
                    throw Fail("document-changed", "The page changed after the target was observed; approve a fresh replay.");
                if (!finalState.Ready) throw Fail("target-changed", "The target is no longer ready.");
            }
            await GateAsync(limit.Token);
            // No retry after dispatch: navigation/click/input may already have taken effect.
            _pendingEffect = host.ExecuteAsync(Copy(step), limit.Token);
            await _pendingEffect.WaitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (!execution.Token.IsCancellationRequested && limit.IsCancellationRequested)
        { throw Fail("step-timeout", "The step exceeded its timeout; its effect will not be retried."); }
        finally { execution.Operations = local.Operations; }
    }

    private static MacroExecutionException Fail(string code, string message) => new(code, message);
}
