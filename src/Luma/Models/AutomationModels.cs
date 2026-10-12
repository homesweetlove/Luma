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
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public override string ToString()
    {
        return Kind switch
        {
            "navigate" => $"Navigate → {Url}",
            "click" => $"Click → {Selector}",
            "input" => $"Input → {Selector} = {Value}",
            _ => Kind
        };
    }
}

public sealed record WatchResult(bool Found, bool Clicked, string? MatchedText, string? Selector);

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
