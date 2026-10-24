using System.Globalization;

namespace Ras.Backend;

public sealed record TemporalInputParseRequest(string Operation, string Text, string CultureName);

/// <summary>
/// The result carries the exact components consumed by DateInputBox and
/// TimeInputBox. Failed TryParse calls still expose their .NET default value
/// (DateTime.MinValue or TimeSpan.Zero); callers use IsValid to keep an
/// incomplete native text draft outside the model.
/// </summary>
public sealed record TemporalInputParseResult(
    string Operation,
    bool IsValid,
    string? CanonicalDate,
    string? CanonicalTime,
    long? TimeSpanTicks,
    int? TimeSpanDays,
    int? TimeSpanHours,
    int? TimeSpanMinutes,
    int? TimeSpanSeconds,
    bool CanSetDateTime);

/// <summary>
/// Uses the retained .NET TryParse implementations with the UI's active
/// culture. Swift Foundation date formatters and hand-written TimeSpan
/// grammars do not accept the same strings across cultures.
/// </summary>
public static class TemporalInputService
{
    public static TemporalInputParseResult Parse(TemporalInputParseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Operation is not ("date" or "time"))
            throw new ArgumentException("The temporal input operation must be date or time.", nameof(request));
        if (request.Text is null)
            throw new ArgumentException("Temporal input text is required.", nameof(request));

        CultureInfo culture;
        try
        {
            if (string.IsNullOrWhiteSpace(request.CultureName))
                throw new ArgumentException("A temporal input culture is required.", nameof(request));
            culture = CultureInfo.GetCultureInfo(request.CultureName);
        }
        catch (CultureNotFoundException exception)
        {
            throw new ArgumentException("The requested temporal input culture is not supported.", nameof(request), exception);
        }

        if (request.Operation == "date")
        {
            bool isValid = DateTime.TryParse(request.Text, culture, DateTimeStyles.None, out DateTime value);
            // DateInputBox.StringToTimeSpan takes only these parsed calendar
            // parts and constructs a new DateTime with its existing hour,
            // minute, and second. That constructor produces Unspecified Kind.
            return new TemporalInputParseResult(
                request.Operation,
                isValid,
                value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                null,
                null,
                null,
                null,
                null,
                null,
                true);
        }

        bool timeIsValid = TimeSpan.TryParse(request.Text, culture, out TimeSpan span);
        int hours = span.Hours;
        int minutes = span.Minutes;
        int seconds = span.Seconds;
        bool canSetDateTime = CanConstructSourceTime(hours, minutes, seconds);
        return new TemporalInputParseResult(
            request.Operation,
            timeIsValid,
            null,
            canSetDateTime
                ? hours.ToString("D2", CultureInfo.InvariantCulture) + ":" +
                    minutes.ToString("D2", CultureInfo.InvariantCulture) + ":" +
                    seconds.ToString("D2", CultureInfo.InvariantCulture)
                : null,
            span.Ticks,
            span.Days,
            hours,
            minutes,
            seconds,
            canSetDateTime);
    }

    private static bool CanConstructSourceTime(int hours, int minutes, int seconds)
    {
        // DateTimeConverters.TimeSpanToDateTime passes TimeSpan.Hours,
        // Minutes, and Seconds directly to the DateTime constructor. It does
        // not normalize a negative TimeSpan; the source therefore throws for
        // component values outside the DateTime constructor's ranges.
        try
        {
            _ = new DateTime(2000, 1, 1, hours, minutes, seconds, 0, 0);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
