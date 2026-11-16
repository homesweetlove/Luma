using Luma.Models;

namespace Luma.Services;

/// <summary>AI suggestions are untrusted JSON only. No model can execute browser actions
/// or replace the current macro without a separate user review.</summary>
public static class AiMacroPlanner
{
    public static string BuildRequest(string task)
    {
        if (string.IsNullOrWhiteSpace(task) || task.Length > 2000)
            throw new ArgumentException("Describe the task in 1–2,000 characters.");
        var safe = DelegatePrivacy.PreparePrompt(task);
        return safe + "\n\nYou are drafting a Luma browser macro, NOT executing one. " +
            "Return ONLY a valid JSON array, without markdown or commentary. " +
            "Each object needs Kind (lowercase). Allowed: navigate with Url, click with Selector, " +
            "wait with Selector and optional Condition, if with Selector/Condition/Steps/ElseSteps, " +
            "loop with LoopCount/Steps, retry with RetryCount/Steps (one click or wait child). " +
            "Allowed conditions: ready, exists, absent, visible, enabled. " +
            "Use only http(s) URLs with NO query, fragment or embedded credentials. " +
            "Never include input steps, credentials, private data, CAPTCHA/OTP bypasses, shell commands, " +
            "payment or destructive actions. Do not invent CSS selectors: if unknown, return [] " +
            "and say nothing else. The user will inspect and explicitly approve every click. " +
            "Example: [{\"Kind\":\"navigate\",\"Url\":\"https://example.com\"}," +
            "{\"Kind\":\"wait\",\"Selector\":\"#ready\",\"Condition\":\"visible\"}].";
    }

    public static string ValidateReply(string response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(response);
        if (response.Length > 16000) throw new ArgumentException("Selected plan is too long.");
        var candidate = response.Trim();
        if (candidate.StartsWith("```", StringComparison.Ordinal))
        {
            var lineEnd = candidate.IndexOf('\n');
            var lastFence = candidate.LastIndexOf("```", StringComparison.Ordinal);
            if (lineEnd < 0 || lastFence <= lineEnd)
                throw new ArgumentException("Select one complete JSON code block.");
            candidate = candidate[(lineEnd + 1)..lastFence].Trim();
        }
        if (!candidate.StartsWith("[", StringComparison.Ordinal) || !candidate.EndsWith("]", StringComparison.Ordinal))
            throw new ArgumentException("The selected AI reply must be a JSON array, not prose.");
        var sandbox = new MacroService();
        sandbox.ImportJson(candidate);
        if (sandbox.Steps.Count is 0 or > 100)
            throw new ArgumentException("A plan must contain between 1 and 100 top-level steps.");
        static void Check(IEnumerable<MacroStep> steps)
        {
            foreach (var step in steps)
            {
                if (step.Kind == "input" || step.AllowSelfHeal)
                    throw new ArgumentException("AI plans cannot submit text or enable self-healing.");
                if (step.Kind == "navigate" && !MacroService.IsRecordableNavigationUrl(step.Url))
                    throw new ArgumentException("AI plans cannot navigate to URLs containing credentials, queries or fragments.");
                if (step.Kind is not ("if" or "loop" or "retry") && (step.Steps.Count != 0 || step.ElseSteps.Count != 0))
                    throw new ArgumentException("Unexpected nested steps in AI plan.");
                Check(step.Steps);
                Check(step.ElseSteps);
            }
        }
        Check(sandbox.Steps);
        return sandbox.ExportJson(); // Re-validate and enforce plaintext privacy restrictions.
    }
}
