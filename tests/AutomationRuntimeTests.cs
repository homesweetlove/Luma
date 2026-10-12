using Luma.Models;
using Luma.Services;

namespace Luma.Automation.Tests;

// Dependency-free behavioral suite. A pure net8.0 runner can link only this file,
// AutomationModels.cs and MacroRuntime.cs, then await RunAllAsync().
public static class AutomationRuntimeTests
{
    public static async Task RunAllAsync()
    {
        await BranchesAndLoops();
        await DelayedDomReadiness();
        await UnsafeTargetsFailClosed();
        await ApprovalAndGate();
        await EffectsAreNeverRetried();
        await CancellationAndSingleFlight();
        await TimeoutsReleaseFlight();
        await SelfHealRequiresOptIn();
        await ProgramValidationBeforeEffects();
        await OperationBudgetStopsEmptyLoops();
        await BoundedReadinessRetry();
        await CancelledApprovalPreventsEffects();
        await PendingEffectRetainsSingleFlight();
        await NavigationDuringApprovalFailsClosed();
        await NavigationDuringInputGateFailsClosed();
    }

    private static MacroStep Click(string selector = "#seat") => new() { Kind = "click", Selector = selector };
    private static MacroRuntime Runtime(FakeHost host, Func<MacroStep, CancellationToken, Task<bool>>? approval = null,
        Func<MacroStep, MacroDomState, CancellationToken, Task<string?>>? heal = null, int duration = 1000, int operations = 1000)
        => new(host, new MacroRuntimeOptions
        {
            ApproveClickAsync = approval, SelfHealAsync = heal, MaxDurationMs = duration, MaxOperations = operations
        });
    private static Task<bool> Approve(MacroStep step, CancellationToken token) => Task.FromResult(true);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static async Task Fails(string code, Func<Task> action)
    {
        try { await action(); }
        catch (MacroExecutionException exception) when (exception.Code == code) { return; }
        throw new Exception("Expected macro failure: " + code);
    }

    private static async Task BranchesAndLoops()
    {
        var host = new FakeHost { Observe = selector => new(selector == "#missing" ? 0 : 1, true, true) };
        var program = new[]
        {
            new MacroStep { Kind = "if", Selector = "#seat", Steps = [Click("#yes")], ElseSteps = [Click("#wrong")] },
            new MacroStep { Kind = "if", Selector = "#missing", Steps = [Click("#wrong")], ElseSteps = [Click("#no")] },
            new MacroStep { Kind = "loop", LoopCount = 3, Steps = [new() { Kind = "input", Selector = "#query", Value = "hello" }] }
        };
        await Runtime(host, Approve).ReplayAsync(program);
        Assert(string.Join(",", host.Effects) == "click:#yes,click:#no,input:#query,input:#query,input:#query", "Branches/loop produced wrong effect sequence.");
    }

    private static async Task DelayedDomReadiness()
    {
        var reads = 0;
        var host = new FakeHost { Observe = _ => new(++reads < 3 ? 0 : 1, true, true) };
        await Runtime(host, Approve).ReplayAsync([new MacroStep { Kind = "wait", Selector = "#seat", TimeoutMs = 300, PollIntervalMs = 10 }, Click()]);
        Assert(reads >= 3 && host.Effects.Count == 1, "A delayed target must be awaited before the click.");
    }

    private static async Task UnsafeTargetsFailClosed()
    {
        foreach (var (state, code) in new[] { (new MacroDomState(2, true, true), "ambiguous-target"), (new MacroDomState(1, true, true, true), "sensitive-input") })
        {
            var host = new FakeHost { Observe = _ => state };
            await Fails(code, () => Runtime(host, Approve).ReplayAsync([Click()]));
            Assert(host.Effects.Count == 0, "An unsafe target received an effect.");
        }
        var missing = new FakeHost { Observe = _ => new(0) };
        try { await Runtime(missing, Approve).ReplayAsync([new MacroStep { Kind = "input", Selector = "#missing", TimeoutMs = 50, PollIntervalMs = 10 }]); throw new Exception("Missing target succeeded."); }
        catch (MacroExecutionException e) when (e.Code is "dom-timeout" or "step-timeout") { }
        Assert(missing.Effects.Count == 0, "Missing target received input.");
    }

    private static async Task ApprovalAndGate()
    {
        var host = new FakeHost();
        await Fails("approval-denied", () => Runtime(host).ReplayAsync([Click()]));
        await Fails("approval-denied", () => Runtime(host, (_, _) => Task.FromResult(false)).ReplayAsync([Click()]));
        await Fails("gate-denied", () => Runtime(host, (_, _) => { host.Allowed = false; return Task.FromResult(true); }).ReplayAsync([Click()]));
        Assert(host.Effects.Count == 0, "Denied approval/gate allowed a click.");

        host = new FakeHost();
        var finalReads = 0;
        host.Observe = _ => new(++finalReads == 1 ? 1 : 0, true, true);
        await Fails("target-changed", () => Runtime(host, Approve).ReplayAsync([Click()]));
        Assert(host.Effects.Count == 0, "DOM changed during approval but still clicked.");
    }

    private static async Task EffectsAreNeverRetried()
    {
        var host = new FakeHost { Effect = (_, _) => throw new InvalidOperationException("Uncertain outcome") };
        try { await Runtime(host, Approve).ReplayAsync([new MacroStep { Kind = "retry", RetryCount = 3, Steps = [Click()] }]); }
        catch (InvalidOperationException) { }
        Assert(host.Effects.Count == 1, "An uncertain effect was retried.");
    }

    private static async Task CancellationAndSingleFlight()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost { Effect = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } };
        var runtime = Runtime(host, Approve);
        using var cancellation = new CancellationTokenSource();
        var running = runtime.ReplayAsync([Click()], cancellation.Token);
        await entered.Task;
        await Fails("busy", () => runtime.ReplayAsync([Click()]));
        cancellation.Cancel();
        try { await running; throw new Exception("Cancellation was ignored."); }
        catch (OperationCanceledException) { }
        await WaitForRelease(runtime);
        Assert(!runtime.IsRunning && host.Effects.Count == 1, "Cancellation did not release single flight.");
    }

    private static async Task TimeoutsReleaseFlight()
    {
        var host = new FakeHost { Effect = (_, token) => Task.Delay(Timeout.Infinite, token) };
        var runtime = Runtime(host, Approve);
        var step = Click(); step.TimeoutMs = 40;
        await Fails("step-timeout", () => runtime.ReplayAsync([step]));
        await WaitForRelease(runtime);
        host.Effect = null;
        await runtime.ReplayAsync([Click()]);
        Assert(host.Effects.Count == 2 && !runtime.IsRunning, "Step timeout prevented the next replay.");
        host.Effect = (_, token) => Task.Delay(Timeout.Infinite, token);
        await Fails("replay-timeout", () => Runtime(host, Approve, duration: 40).ReplayAsync([Click()]));
    }

    private static async Task SelfHealRequiresOptIn()
    {
        var calls = 0;
        var host = new FakeHost { Observe = selector => new(selector == "#fixed" ? 1 : 0, true, true) };
        Task<string?> Heal(MacroStep step, MacroDomState state, CancellationToken token) { calls++; return Task.FromResult<string?>("#fixed"); }
        var step = Click("#old"); step.TimeoutMs = 50; step.PollIntervalMs = 10;
        try { await Runtime(host, Approve, Heal).ReplayAsync([step]); }
        catch (MacroExecutionException e) when (e.Code is "dom-timeout" or "step-timeout") { }
        Assert(calls == 0 && host.Effects.Count == 0, "Self-heal ran without step opt-in.");
        step.AllowSelfHeal = true; step.TimeoutMs = 300;
        string? approvedSelector = null;
        await Runtime(host, (effective, _) => { approvedSelector = effective.Selector; return Task.FromResult(true); }, Heal).ReplayAsync([step]);
        Assert(calls == 1 && approvedSelector == "#fixed" && host.Effects.Single() == "click:#fixed" && step.Selector == "#old", "Healing must approve the new selector and preserve the source program.");
    }

    private static async Task ProgramValidationBeforeEffects()
    {
        var host = new FakeHost();
        await Fails("invalid-kind", () => Runtime(host, Approve).ReplayAsync([Click(), new() { Kind = "bogus" }]));
        await Fails("invalid-program", () => Runtime(host, Approve).ReplayAsync([new() { Kind = "loop", Steps = null! }]));
        var cyclic = new MacroStep { Kind = "loop" }; cyclic.Steps.Add(cyclic);
        await Fails("invalid-program", () => Runtime(host, Approve).ReplayAsync([cyclic]));
        await Fails("invalid-url", () => Runtime(host, Approve).ReplayAsync([new() { Kind = "navigate", Url = "javascript:alert(1)" }]));
        Assert(host.Effects.Count == 0, "Invalid program caused a partial execution.");
    }

    private static async Task OperationBudgetStopsEmptyLoops()
    {
        var host = new FakeHost();
        await Fails("operation-limit", () => Runtime(host, Approve, operations: 5).ReplayAsync([new MacroStep { Kind = "loop", LoopCount = 10 }]));
        Assert(host.Effects.Count == 0, "Empty loops exceeded the execution budget.");
    }

    private static async Task BoundedReadinessRetry()
    {
        var reads = 0;
        var host = new FakeHost { Observe = _ => new(++reads >= 3 ? 1 : 0, true, true) };
        var click = Click(); click.TimeoutMs = 600; click.PollIntervalMs = 1000;
        await Runtime(host, Approve).ReplayAsync([new MacroStep { Kind = "retry", RetryCount = 2, RetryDelayMs = 0, Steps = [click] }]);
        Assert(reads >= 3 && host.Effects.Count == 1, "Readiness retries must allow one final effect.");
    }

    private static async Task CancelledApprovalPreventsEffects()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost();
        using var cancellation = new CancellationTokenSource();
        var runtime = Runtime(host, async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return true;
        });
        var running = runtime.ReplayAsync([Click()], cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        try { await running; throw new Exception("Approval cancellation was ignored."); }
        catch (OperationCanceledException) { }
        Assert(host.Effects.Count == 0 && !runtime.IsRunning, "A cancelled approval dispatched an effect.");
    }

    private static async Task PendingEffectRetainsSingleFlight()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost { Effect = (_, _) => { entered.TrySetResult(); return pending.Task; } };
        var runtime = Runtime(host, Approve);
        using var cancellation = new CancellationTokenSource();
        var running = runtime.ReplayAsync([Click()], cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        try { await running; throw new Exception("Caller cancellation was ignored."); }
        catch (OperationCanceledException) { }
        Assert(runtime.IsRunning, "An unresolved dispatched effect released single flight.");
        await Fails("busy", () => runtime.ReplayAsync([Click()]));
        Assert(host.Effects.Count == 1, "A replay overlapped the unresolved effect.");
        pending.SetResult();
        await WaitForRelease(runtime);
        Assert(!runtime.IsRunning, "Completing the effect failed to release single flight.");
        host.Effect = null;
        await runtime.ReplayAsync([Click()]);
        Assert(host.Effects.Count == 2, "Replay could not resume after the unresolved effect completed.");
    }

    private static async Task WaitForRelease(MacroRuntime runtime)
    {
        for (var i = 0; runtime.IsRunning && i < 100; i++) await Task.Delay(5);
        Assert(!runtime.IsRunning, "The completed host effect retained single flight.");
    }

    private static async Task NavigationDuringApprovalFailsClosed()
    {
        foreach (var reload in new[] { false, true })
        {
            var source = "https://tickets.example/seats";
            var document = "document-1";
            var host = new FakeHost { Observe = _ => new(1, true, true, Source: source, DocumentId: document) };
            await Fails("document-changed", () => Runtime(host, (approved, _) =>
            {
                Assert(approved.ExpectedSource == source && approved.ExpectedDocumentId == document, "Approval was not bound to its observed document.");
                if (reload) document = "document-2";
                else source = "https://other.example/seats";
                return Task.FromResult(true);
            }).ReplayAsync([Click()]));
            Assert(host.Effects.Count == 0, "Navigation/reload during approval allowed an effect on the new document.");
        }
    }

    private static async Task NavigationDuringInputGateFailsClosed()
    {
        var source = "https://search.example/";
        var gates = 0;
        var host = new FakeHost
        {
            Observe = _ => new(1, true, true, Source: source, DocumentId: "document-1"),
            Gate = () => { if (++gates == 2) source = "https://other.example/"; return true; }
        };
        await Fails("document-changed", () => Runtime(host).ReplayAsync([new MacroStep { Kind = "input", Selector = "#query", Value = "public search" }]));
        Assert(host.Effects.Count == 0, "Input was sent after the gate navigated to a different page.");
    }

    private sealed class FakeHost : IMacroHost
    {
        public bool Allowed { get; set; } = true;
        public Func<bool>? Gate { get; set; }
        public Func<string, MacroDomState> Observe { get; set; } = _ => new(1, true, true);
        public Func<MacroStep, CancellationToken, Task>? Effect { get; set; }
        public List<string> Effects { get; } = [];
        public Task<bool> CheckGateAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Gate?.Invoke() ?? Allowed); }
        public Task<MacroDomState> ObserveAsync(string selector, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Observe(selector)); }
        public Task ExecuteAsync(MacroStep step, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Allowed) throw new Exception("Effect dispatched with closed gate.");
            Effects.Add(step.Kind + ":" + step.Selector);
            return Effect?.Invoke(step, token) ?? Task.CompletedTask;
        }
    }
}
