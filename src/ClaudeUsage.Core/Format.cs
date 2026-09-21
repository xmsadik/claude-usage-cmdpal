using System.Globalization;

namespace ClaudeUsage.Core;

/// <summary>
/// Number/text formatting shared by the Dock band and the detail page. Everything here uses
/// <see cref="CultureInfo.InvariantCulture"/> — the host machine may be non-English, and
/// "{0:N1}" under e.g. tr-TR prints "37,0", not "37.0".
/// </summary>
public static class Format
{
    /// <summary>1.2K / 3.4M / 1.1B, one decimal place, invariant decimal point.</summary>
    public static string Tokens(double n)
    {
        if (n >= 1_000_000_000)
        {
            return (n / 1_000_000_000).ToString("N1", CultureInfo.InvariantCulture) + "B";
        }

        if (n >= 1_000_000)
        {
            return (n / 1_000_000).ToString("N1", CultureInfo.InvariantCulture) + "M";
        }

        if (n >= 1_000)
        {
            return (n / 1_000).ToString("N1", CultureInfo.InvariantCulture) + "K";
        }

        return ((long)n).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// "resets in 2d 3h" / "resets in 2h 14m" / "resets in 14m" / "resets now". Every split
    /// floors (never rounds) so e.g. 20h59m never inflates to "1d" — ports the ps1
    /// reference's Format-Countdown exactly, including the comment about [int] rounding.
    /// </summary>
    public static string Countdown(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is not DateTimeOffset when)
        {
            return string.Empty;
        }

        var seconds = (when - now).TotalSeconds;
        if (seconds <= 0)
        {
            return "resets now";
        }

        var totalMinutes = (long)Math.Floor(seconds / 60);
        var totalHours = (long)Math.Floor(totalMinutes / 60.0);
        var minutes = totalMinutes % 60;
        var days = (long)Math.Floor(totalHours / 24.0);
        var hours = totalHours % 24;

        if (days > 0)
        {
            return $"resets in {days}d {hours}h";
        }

        if (hours > 0)
        {
            return $"resets in {hours}h {minutes}m";
        }

        return $"resets in {minutes}m";
    }

    /// <summary>A block-character meter, e.g. "██████░░░░░░░░░░". Rounds to nearest (banker's, matching [math]::Round).</summary>
    public static string Bar(double fraction, int width)
    {
        var clamped = Math.Clamp(fraction, 0.0, 1.0);
        var filled = Math.Clamp((int)Math.Round(clamped * width, MidpointRounding.ToEven), 0, width);
        return new string('█', filled) + new string('░', width - filled);
    }
}
