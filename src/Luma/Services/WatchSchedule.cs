using System.Globalization;
using System.Text.RegularExpressions;

namespace Luma.Services;

public static class WatchSchedule
{
    public static bool TryParse(string startText, string endText, DateTimeOffset now,
        out DateTimeOffset start, out DateTimeOffset end)
    {
        start = end = default;
        const string iso = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?(?:Z|[+-]\d{2}:\d{2})$";
        return Regex.IsMatch(startText ?? "", iso) && Regex.IsMatch(endText ?? "", iso) &&
            DateTimeOffset.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.None, out start) &&
            DateTimeOffset.TryParse(endText, CultureInfo.InvariantCulture, DateTimeStyles.None, out end) &&
            start < end && end > now && end - start <= TimeSpan.FromHours(24);
    }
}
