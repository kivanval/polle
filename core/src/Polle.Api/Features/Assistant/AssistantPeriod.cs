using System.Globalization;
using System.Text.RegularExpressions;

namespace Polle.Api.Features.Assistant;

/// <summary>What one message says about the period: a start month (YYYY-MM) and a horizon in whole years, each when given.</summary>
public sealed record PeriodParts(string? StartMonth, int? Years)
{
    public bool IsEmpty => StartMonth is null && Years is null;
}

/// <summary>
/// The period the assistant plans for, and where each part came from, so the answer can say which values it assumed.
/// </summary>
/// <param name="StartAssumed">No start was given anywhere: next month.</param>
/// <param name="PastStart">A start that had already passed, moved to this month (plans look ahead); null otherwise.</param>
public sealed record ResolvedPeriod(string StartMonth, int Years, bool StartAssumed, bool YearsAssumed, string? PastStart = null)
{
    public bool AnyAssumed => StartAssumed || YearsAssumed || PastStart is not null;

    /// <summary>What the answer says it assumed, in a short sentence; null when the farmer gave the whole period.</summary>
    public string? AssumedText(bool uk)
    {
        var parts = new List<string>();
        if (PastStart is not null) parts.Add(uk ? $"{PastStart} уже минув, тож починаю з {StartMonth}" : $"{PastStart} has passed, so I start in {StartMonth}");
        else if (StartAssumed) parts.Add(uk ? "початок не вказано, тож беру наступний місяць" : "no start given, so I use next month");
        if (YearsAssumed) parts.Add(uk ? $"тривалість не вказано, тож беру {Years} років" : $"no length given, so I use {Years} years");
        if (parts.Count == 0) return null;
        var text = string.Join(". ", parts.Select(part => $"{char.ToUpperInvariant(part[0])}{part[1..]}"));
        return $"{char.ToUpperInvariant(text[0])}{text[1..]}. " + (uk ? "Напишіть, наприклад, «з жовтня 2027 на 10 років», щоб змінити."
            : "Say e.g. \"from October 2027 for 10 years\" to change it.");
    }

    public string EndMonth => DateOnly.ParseExact($"{StartMonth}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture)
        .AddMonths(Years * 12 - 1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

/// <summary>
/// Reads the planning period from natural language and carries it through the conversation, so the assistant never
/// questions the farmer for a format. Each part comes from the newest place that gives it: the current message, then the
/// period the conversation already used, then the farmer's earlier messages, then the fallback (a field's last strategy
/// run), then the defaults: next month and <see cref="DefaultYears"/> years. "2026-10" and then "10" therefore mean
/// October 2026 for ten years. Understands "2026-10", "з жовтня 2026", "October 2027", "з наступного року", "next
/// month", "на 10 років", "for five years", "36 months", a season ("з осені" = September) and a bare number of years
/// sent as a reply.
/// </summary>
public static partial class AssistantPeriod
{
    public const int DefaultYears = 5;
    public const int MaxYears = 20;

    private static readonly Dictionary<string, int> Months = new(StringComparer.Ordinal)
    {
        ["січень"] = 1, ["січня"] = 1, ["січні"] = 1, ["лютий"] = 2, ["лютого"] = 2, ["лютому"] = 2,
        ["березень"] = 3, ["березня"] = 3, ["березні"] = 3, ["квітень"] = 4, ["квітня"] = 4, ["квітні"] = 4,
        ["травень"] = 5, ["травня"] = 5, ["травні"] = 5, ["червень"] = 6, ["червня"] = 6, ["червні"] = 6,
        ["липень"] = 7, ["липня"] = 7, ["липні"] = 7, ["серпень"] = 8, ["серпня"] = 8, ["серпні"] = 8,
        ["вересень"] = 9, ["вересня"] = 9, ["вересні"] = 9, ["жовтень"] = 10, ["жовтня"] = 10, ["жовтні"] = 10,
        ["листопад"] = 11, ["листопада"] = 11, ["листопаді"] = 11, ["грудень"] = 12, ["грудня"] = 12, ["грудні"] = 12,
        ["january"] = 1, ["jan"] = 1, ["february"] = 2, ["feb"] = 2, ["march"] = 3, ["mar"] = 3, ["april"] = 4, ["apr"] = 4,
        ["may"] = 5, ["june"] = 6, ["jun"] = 6, ["july"] = 7, ["jul"] = 7, ["august"] = 8, ["aug"] = 8,
        ["september"] = 9, ["sep"] = 9, ["sept"] = 9, ["october"] = 10, ["oct"] = 10, ["november"] = 11, ["nov"] = 11,
        ["december"] = 12, ["dec"] = 12,
    };

    private static readonly Dictionary<string, int> Seasons = new(StringComparer.Ordinal)
    {
        ["весна"] = 3, ["весни"] = 3, ["навесні"] = 3, ["spring"] = 3,
        ["літо"] = 6, ["літа"] = 6, ["влітку"] = 6, ["summer"] = 6,
        ["осінь"] = 9, ["осені"] = 9, ["восени"] = 9, ["autumn"] = 9, ["fall"] = 9,
        ["зима"] = 12, ["зими"] = 12, ["взимку"] = 12, ["winter"] = 12,
    };

    private static readonly Dictionary<string, int> Numbers = new(StringComparer.Ordinal)
    {
        ["один"] = 1, ["одного"] = 1, ["одна"] = 1, ["два"] = 2, ["дві"] = 2, ["двох"] = 2, ["три"] = 3, ["трьох"] = 3,
        ["чотири"] = 4, ["чотирьох"] = 4, ["п'ять"] = 5, ["п'яти"] = 5, ["шість"] = 6, ["шести"] = 6, ["сім"] = 7, ["семи"] = 7,
        ["вісім"] = 8, ["восьми"] = 8, ["дев'ять"] = 9, ["дев'яти"] = 9, ["десять"] = 10, ["десяти"] = 10,
        ["one"] = 1, ["a"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
    };

    /// <summary>
    /// The period for this turn. <paramref name="fallback"/> stands in before the defaults, e.g. the period of the field's
    /// last strategy run. A start before this month moves to this month: plans look ahead.
    /// </summary>
    public static ResolvedPeriod Resolve(string message, IReadOnlyList<AssistantHistoryItem> history, AssistantConversationContext? previous,
        DateOnly today, PeriodParts? fallback = null)
    {
        var sources = new List<PeriodParts> { Read(message, today, reply: true) };
        if (previous is { StartMonth: not null } or { PlanningHorizonYears: not null })
            sources.Add(new PeriodParts(previous.StartMonth, previous.PlanningHorizonYears));
        // Earlier messages, newest first; a bare number counts as years only in the message itself.
        sources.AddRange(history.Where(item => item.Role == "user").Reverse().Select(item => Read(item.Content, today, reply: false)));
        if (fallback is not null) sources.Add(fallback);

        var start = sources.Select(parts => parts.StartMonth).FirstOrDefault(value => value is not null);
        var years = sources.Select(parts => parts.Years).FirstOrDefault(value => value is not null);
        var next = new DateOnly(today.Year, today.Month, 1).AddMonths(1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var thisMonth = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var past = start is not null && string.CompareOrdinal(start, thisMonth) < 0 ? start : null;
        return new ResolvedPeriod(start is null ? next : past is not null ? thisMonth : start, Math.Clamp(years ?? DefaultYears, 1, MaxYears),
            start is null, years is null, past);
    }

    /// <summary>
    /// What one message says about the period. With <paramref name="reply"/>, a message that is only a number (e.g. "10"
    /// after the assistant mentioned the horizon) is that many years.
    /// </summary>
    public static PeriodParts Read(string message, DateOnly today, bool reply = false)
    {
        var text = message.ToLowerInvariant().Replace('’', '\'').Replace('ʼ', '\'').Replace('`', '\'');
        return new PeriodParts(Start(text, today), Years(text, reply));
    }

    /// <summary>A message that says nothing but a period ("10", "2026-10", "на 10 років", "з жовтня 2026"): a reply to the last tool.</summary>
    public static bool IsPeriodOnly(string message, DateOnly today)
    {
        var text = message.ToLowerInvariant().Replace('’', '\'');
        if (Read(text, today, reply: true).IsEmpty) return false;
        var rest = PeriodWordsRegex().Replace(text, " ");
        return !LetterRegex().IsMatch(rest);
    }

    private static string? Start(string text, DateOnly today)
    {
        if (IsoMonthRegex().Match(text) is { Success: true } iso)
        {
            var month = int.Parse(iso.Groups[2].Value, CultureInfo.InvariantCulture);
            if (month is >= 1 and <= 12) return Month(int.Parse(iso.Groups[1].Value, CultureInfo.InvariantCulture), month);
        }
        if (NextYearRegex().IsMatch(text)) return Month(today.Year + 1, 1);
        if (NextMonthRegex().IsMatch(text)) return Month(new DateOnly(today.Year, today.Month, 1).AddMonths(1));
        if (ThisMonthRegex().IsMatch(text)) return Month(today.Year, today.Month);

        var years = YearRegex().Matches(text).Where(match => !IsDuration(text, match)).Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
        int? named = null;
        foreach (Match word in WordRegex().Matches(text))
        {
            if (Months.TryGetValue(word.Value, out var month) && (word.Value != "may" || years.Length > 0)) { named = month; break; }
            if (Seasons.TryGetValue(word.Value, out var season)) { named ??= season; }
        }
        if (named is { } m)
        {
            if (years.Length > 0) return Month(years[0], m);
            // A month without a year is its next occurrence: this month counts.
            var year = m >= today.Month ? today.Year : today.Year + 1;
            return Month(year, m);
        }
        return years.Length > 0 ? Month(years[0], 1) : null;
    }

    private static int? Years(string text, bool reply)
    {
        if (YearCountRegex().Match(text) is { Success: true } count) return int.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture);
        foreach (Match match in WordCountRegex().Matches(text))
            if (Numbers.TryGetValue(match.Groups[1].Value, out var value))
                return match.Groups[2].Value.StartsWith("міс", StringComparison.Ordinal) || match.Groups[2].Value.StartsWith("month", StringComparison.Ordinal)
                    ? Math.Max(1, (int)Math.Ceiling(value / 12.0)) : value;
        if (MonthCountRegex().Match(text) is { Success: true } months)
            return Math.Max(1, (int)Math.Ceiling(int.Parse(months.Groups[1].Value, CultureInfo.InvariantCulture) / 12.0));
        if (OneYearRegex().IsMatch(text)) return 1;
        if (reply && BareNumberRegex().Match(text) is { Success: true } bare)
        {
            var value = int.Parse(bare.Groups[1].Value, CultureInfo.InvariantCulture);
            if (value is >= 1 and <= 30) return value;
        }
        return null;
    }

    /// <summary>"2030" in "до 2030 року" ends the horizon rather than starting it; "2027 рік" or "for 2027" still starts it.</summary>
    private static bool IsDuration(string text, Match year) => year.Index >= 3 && UntilRegex().IsMatch(text[..year.Index]);

    private static string Month(int year, int month) => $"{year:D4}-{month:D2}";
    private static string Month(DateOnly date) => Month(date.Year, date.Month);

    [GeneratedRegex(@"(?<!\d)((?:19|20)\d{2})[-./](\d{1,2})(?!\d)")]
    private static partial Regex IsoMonthRegex();

    [GeneratedRegex(@"(?<!\d)(?:19|20)\d{2}(?!\d)")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\w+(?:'\w+)?")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"(?:до|по|until|through|till)\s*$")]
    private static partial Regex UntilRegex();

    [GeneratedRegex(@"наступн\w*\s+рок\w*|next\s+year")]
    private static partial Regex NextYearRegex();

    [GeneratedRegex(@"наступн\w*\s+місяц\w*|next\s+month")]
    private static partial Regex NextMonthRegex();

    [GeneratedRegex(@"цього\s+місяця|this\s+month")]
    private static partial Regex ThisMonthRegex();

    [GeneratedRegex(@"(?<![\d.,-])(\d{1,2})\s*-?\s*(?:рік|року|роки|років|р\.|річн\w*|years?|yrs?|yr)(?!\w)")]
    private static partial Regex YearCountRegex();

    [GeneratedRegex(@"(?<![\d.,-])(\d{1,3})\s*-?\s*(?:місяц\w*|місяч\w*|міс\.?|months?|mos?)(?!\w)")]
    private static partial Regex MonthCountRegex();

    [GeneratedRegex(@"(?<!\w)(\w+(?:'\w+)?)\s*-?\s*(рік|року|роки|років|year|years|місяць|місяці|місяців|month|months)(?!\w)")]
    private static partial Regex WordCountRegex();

    [GeneratedRegex(@"(?<!\w)на\s+(?:один\s+)?рік(?!\w)|\bfor\s+(?:a|one)\s+year\b|\bone-year\b|(?<!\w)однорічн\w*")]
    private static partial Regex OneYearRegex();

    [GeneratedRegex(@"^\s*(\d{1,2})\s*[.!]?\s*$")]
    private static partial Regex BareNumberRegex();

    [GeneratedRegex(@"\d+|[-–./,:;!?]|\b(?:з|із|від|на|до|у|в|по|for|from|starting|start|in|of|the|a|until|next|this|year|years|yrs?|month|months|рік|року|роки|років|р|місяць|місяця|місяці|місяців|наступного|наступний|цього|starting\s+in)\b|" +
        @"січ\w*|лют\w*|берез\w*|квіт\w*|трав\w*|черв\w*|лип\w*|серп\w*|верес\w*|жовт\w*|листопад\w*|груд\w*|весн\w*|навесні|літ\w*|влітку|осен\w*|осін\w*|восени|зим\w*|взимку|" +
        @"\b(?:january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sept?|oct|nov|dec|spring|summer|autumn|fall|winter)\b|" +
        @"\b(?:one|two|three|four|five|six|seven|eight|nine|ten)\b|один|одного|два|дві|три|чотири|п'ять|шість|сім|вісім|дев'ять|десять")]
    private static partial Regex PeriodWordsRegex();

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex LetterRegex();
}
