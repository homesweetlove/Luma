using System.Text.Json.Serialization;

namespace Luma.Models;

public sealed class BrowserSnapshotItem
{
    [JsonPropertyName("ref")]
    public string Ref { get; set; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("selector")]
    public string Selector { get; set; } = string.Empty;

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }
}

public sealed class MacroStep
{
    public string Kind { get; set; } = string.Empty;
    public string? Selector { get; set; }
    public string? Value { get; set; }
    public string? Url { get; set; }
    private int? _timeoutMs;
    public int TimeoutMs { get => _timeoutMs ?? (Kind == "click" ? 30000 : 5000); set => _timeoutMs = value; }
    public int PollIntervalMs { get; set; } = 100;
    public int RetryCount { get; set; }
    public int RetryDelayMs { get; set; } = 200;
    public int LoopCount { get; set; } = 1;
    public string Condition { get; set; } = "ready";
    public List<MacroStep> Steps { get; set; } = [];
    public List<MacroStep> ElseSteps { get; set; } = [];
    public bool AllowSelfHeal { get; set; }
    [JsonIgnore]
    public string? ExpectedSource { get; set; }
    [JsonIgnore]
    public string? ExpectedDocumentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public override string ToString()
    {
        return Kind switch
        {
            "navigate" => $"Navigate → {Url}",
            "click" => $"Click → {Selector}",
            "input" => $"Input → {Selector} (value hidden)",
            _ => Kind
        };
    }
}

public sealed record WatchResult(bool Found, bool Clicked, string? MatchedText, string? Selector)
{
    public int MatchCount { get; init; }
    public string? Error { get; init; }
    public string? Source { get; init; }
    public string? DocumentId { get; init; }
}

public enum SensitiveAction
{
    Read,
    Navigate,
    Download,
    SendEmail,
    CreateCalendarEvent,
    ConfirmReservation,
    Purchase,
    DeleteData,
    GitPush,
    ExecuteShell
}

public sealed record ApprovalDecision(bool RequiresApproval, string Reason);

public sealed record AgentIntent(string Kind, string? Argument = null);
