using Luma.Models;

namespace Luma.Services;

public sealed class LocalIntentRouter
{
    public AgentIntent Parse(string input)
    {
        var text = (input ?? string.Empty).Trim();
        var lower = text.ToLowerInvariant();

        if (lower.Contains("스냅샷") || lower.Contains("snapshot") || lower.Contains("화면 읽") || lower.Contains("페이지 읽"))
        {
            return new AgentIntent("snapshot");
        }

        // Stop commands must win before the generic "macro recording" start command.
        // Otherwise "매크로 녹화 중지" incorrectly starts recording.
        if (lower.Contains("녹화 중지") || lower.Contains("녹화 끝") ||
            lower.Contains("녹화 종료") || lower.Contains("녹화 멈춰") ||
            lower.Contains("stop recording"))
        {
            return new AgentIntent("stop-recording");
        }

        if ((lower.Contains("매크로") && lower.Contains("녹화")) || lower.Contains("record macro") || lower == "record")
        {
            return new AgentIntent("record");
        }

        if ((lower.Contains("매크로") && lower.Contains("재생")) || lower.Contains("replay macro") || lower == "replay")
        {
            return new AgentIntent("replay");
        }

        if (lower.StartsWith("watch ", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentIntent("watch", text[6..].Trim());
        }

        var marker = "자리 생기면";
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index > 0)
        {
            var target = text[..index].Trim();
            return new AgentIntent("watch", target);
        }

        if (lower.Contains("감시해") || lower.Contains("지켜봐"))
        {
            var target = text
                .Replace("감시해", string.Empty, StringComparison.Ordinal)
                .Replace("지켜봐", string.Empty, StringComparison.Ordinal)
                .Trim();
            return new AgentIntent("watch", target);
        }

        return new AgentIntent("provider", text);
    }
}
