using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using LogsParser.Diagnostics;
using LogsParser.Exceptions;
using LogsParser.Models;
using LogsParser.Parsing;

namespace LogsParser;

public static partial class LogsHtmlParser
{
    private static ILogger Logger => LogsParserLogging.CreateLogger(nameof(LogsHtmlParser));

    public static LogsPage ParseLogs(string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        var account = LogsAccountParser.Parse(html);
        var tbody = HtmlFragmentReader.ExtractFirstTagInnerHtml(html, "tbody");
        if (string.IsNullOrWhiteSpace(tbody))
        {
            return new LogsPage(Array.Empty<LogEntry>(), ParseMetaInfo(html), account);
        }

        var entries = HtmlFragmentReader.ExtractTableRows(tbody)
            .Select(ParseLogEntry)
            .Where(static entry => entry is not null)
            .Cast<LogEntry>()
            .ToArray();

        return new LogsPage(entries, ParseMetaInfo(html), account);
    }

    public static AdminActivityReport ParseAdminActivity(string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        var periodMatch = AdminPeriodRegex().Match(WebUtility.HtmlDecode(html));
        if (!periodMatch.Success)
        {
            Logger.LogWarning("Admin activity period was not found in source html.");
            throw new HtmlParsingException("Admin activity period was not found.");
        }

        var period = (
            ParseDateTime(periodMatch.Groups["from"].Value),
            ParseDateTime(periodMatch.Groups["to"].Value));

        var tbody = HtmlFragmentReader.ExtractFirstTagInnerHtml(html, "tbody")
            ?? throw new HtmlParsingException("Admin activity table was not found.");

        var entries = HtmlFragmentReader.ExtractTableRows(tbody)
            .Select(ParseAdminActivityEntry)
            .Where(static entry => entry is not null)
            .Cast<AdminActivityEntry>()
            .ToArray();

        var metaInfo = new AdminActivityMetaInfo(
            entries.Length,
            Math.Max(0, (period.Item2 - period.Item1).Days),
            entries.Sum(static entry => entry.TotalReports),
            entries.Sum(static entry => entry.TotalBans));

        return new AdminActivityReport(period, entries, metaInfo);
    }

    public static TopOperationsReport ParseTopOperations(string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        var dateMatch = TopDateRegex().Match(WebUtility.HtmlDecode(html));
        if (!dateMatch.Success)
        {
            Logger.LogWarning("Top operations date was not found in source html.");
            throw new HtmlParsingException("Top operations date was not found.");
        }

        var date = DateOnly.ParseExact(dateMatch.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var tbody = HtmlFragmentReader.ExtractFirstTagInnerHtml(html, "tbody")
            ?? throw new HtmlParsingException("Top operations table was not found.");

        var entries = HtmlFragmentReader.ExtractTableRows(tbody)
            .Select(ParseTopOperationsEntry)
            .Where(static entry => entry is not null)
            .Cast<TopOperationsEntry>()
            .ToArray();

        var metaInfo = new TopOperationsMetaInfo(
            entries.Length,
            entries.Sum(static entry => entry.TotalTransactions),
            entries.Aggregate(0UL, static (acc, entry) => acc + entry.Sum));

        return new TopOperationsReport(date, entries, metaInfo);
    }

    private static LogEntry? ParseLogEntry(string rowHtml)
    {
        var cells = HtmlFragmentReader.ExtractTableCells(HtmlFragmentReader.ExtractInnerHtml(rowHtml), "td");
        if (cells.Count < 2)
        {
            return null;
        }

        var timestamp = ParseDateTime(HtmlFragmentReader.NormalizeText(cells[0]));
        var logHtml = HtmlFragmentReader.ExtractInnerHtml(cells[1]).Trim();
        var logText = HtmlFragmentReader.NormalizeText(logHtml);

        int? money = null;
        int? bank = null;
        int? donate = null;
        LogAdditionalInfo? additionalInfo = null;
        LogParticipant? target = null;
        LogParticipant? sender = null;

        if (cells.Count >= 3)
        {
            var codeValues = HtmlFragmentReader.ExtractTopLevelBlocks(HtmlFragmentReader.ExtractInnerHtml(cells[2]), "code")
                .Select(HtmlFragmentReader.ExtractInnerHtml)
                .Select(HtmlFragmentReader.NormalizeText)
                .ToArray();

            if (codeValues.Length >= 4)
            {
                money = ParseInt(codeValues[1]);
                bank = ParseInt(codeValues[2]);
                donate = ParseInt(codeValues[3]);
            }

            additionalInfo = ParseAdditionalInfo(cells[2]);
        }

        if (cells.Count >= 4)
        {
            (sender, target) = ParseParticipants(cells[3]);
        }

        return new LogEntry(timestamp, logText, logHtml, money, bank, donate, additionalInfo, target, sender);
    }

    private static LogAdditionalInfo? ParseAdditionalInfo(string accountCellHtml)
    {
        var hiddenBlock = HtmlFragmentReader.ExtractFirstElementByClass(accountCellHtml, "app__hidden");
        if (hiddenBlock is null)
        {
            return null;
        }

        var liValues = HtmlFragmentReader.ExtractTopLevelBlocks(HtmlFragmentReader.ExtractInnerHtml(hiddenBlock), "li")
            .SelectMany(static li =>
                HtmlFragmentReader.ExtractTopLevelBlocks(HtmlFragmentReader.ExtractInnerHtml(li), "code")
                    .Select(HtmlFragmentReader.ExtractInnerHtml)
                    .Select(HtmlFragmentReader.NormalizeText))
            .ToArray();

        if (liValues.Length < 10)
        {
            return null;
        }

        return new LogAdditionalInfo(
            ParseInt(liValues[0]),
            ParseLong(liValues[1]),
            ParseLong(liValues[2]),
            ParseLong(liValues[3]),
            ParseLong(liValues[4]),
            ParseLong(liValues[5]),
            ParseLong(liValues[6]),
            ParseLong(liValues[7]),
            ParseLong(liValues[8]),
            ParseInt(liValues[9]));
    }

    private static (LogParticipant? Sender, LogParticipant? Target) ParseParticipants(string participantsCellHtml)
    {
        LogParticipant? sender = null;
        LogParticipant? target = null;

        foreach (var element in HtmlFragmentReader.ExtractElementsByClass(participantsCellHtml, "table-ip"))
        {
            var innerHtml = HtmlFragmentReader.ExtractInnerHtml(element);
            var codes = HtmlFragmentReader.ExtractTopLevelBlocks(innerHtml, "code");
            var links = HtmlFragmentReader.ExtractTopLevelBlocks(innerHtml, "a");
            if (codes.Count == 0 || links.Count < 2)
            {
                continue;
            }

            var kind = HtmlFragmentReader.NormalizeText(HtmlFragmentReader.ExtractInnerHtml(codes[0]));
            var lastIp = HtmlFragmentReader.NormalizeText(HtmlFragmentReader.ExtractInnerHtml(links[0]));
            var registrationIp = HtmlFragmentReader.NormalizeText(HtmlFragmentReader.ExtractInnerHtml(links[1]));
            var participant = new LogParticipant(lastIp, registrationIp);

            if (string.Equals(kind, "I:", StringComparison.OrdinalIgnoreCase))
            {
                sender = participant;
            }
            else if (string.Equals(kind, "II:", StringComparison.OrdinalIgnoreCase))
            {
                target = participant;
            }
        }

        return (sender, target);
    }

    private static LogPageMetaInfo? ParseMetaInfo(string html)
    {
        var match = MetaInfoRegex().Match(WebUtility.HtmlDecode(html));
        if (!match.Success)
        {
            return null;
        }

        return new LogPageMetaInfo(
            ParseInt(match.Groups["start"].Value),
            ParseInt(match.Groups["end"].Value),
            ParseInt(match.Groups["total"].Value));
    }

    private static AdminActivityEntry? ParseAdminActivityEntry(string rowHtml)
    {
        var cells = HtmlFragmentReader.ExtractTableCells(HtmlFragmentReader.ExtractInnerHtml(rowHtml), "td");
        if (cells.Count < 10)
        {
            return null;
        }

        return new AdminActivityEntry(
            HtmlFragmentReader.NormalizeText(cells[0]),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[1])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[2])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[3])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[4])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[5])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[6])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[7])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[8])),
            HtmlFragmentReader.NormalizeText(cells[9]),
            ParseAdminActivityDetails(rowHtml));
    }

    private static IReadOnlyList<AdminActivityDay> ParseAdminActivityDetails(string rowHtml)
    {
        var hiddenBlock = HtmlFragmentReader.ExtractFirstElementByClass(rowHtml, "app__hidden");
        if (hiddenBlock is null)
        {
            return Array.Empty<AdminActivityDay>();
        }

        return HtmlFragmentReader.ExtractTableRows(HtmlFragmentReader.ExtractInnerHtml(hiddenBlock))
            .Skip(1)
            .Select(ParseAdminActivityDay)
            .Where(static detail => detail is not null)
            .Cast<AdminActivityDay>()
            .ToArray();
    }

    private static AdminActivityDay? ParseAdminActivityDay(string rowHtml)
    {
        var cells = HtmlFragmentReader.ExtractTableCells(HtmlFragmentReader.ExtractInnerHtml(rowHtml), "th");
        if (cells.Count < 7)
        {
            return null;
        }

        return new AdminActivityDay(
            DateTime.ParseExact(HtmlFragmentReader.NormalizeText(cells[0]), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            ParseTimeSpan(HtmlFragmentReader.NormalizeText(cells[1])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[2])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[3])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[4])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[5])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[6])));
    }

    private static TopOperationsEntry? ParseTopOperationsEntry(string rowHtml)
    {
        var cells = HtmlFragmentReader.ExtractTableCells(HtmlFragmentReader.ExtractInnerHtml(rowHtml), "td");
        if (cells.Count < 6)
        {
            return null;
        }

        return new TopOperationsEntry(
            HtmlFragmentReader.NormalizeText(cells[0]),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[1])),
            IPAddress.Parse(HtmlFragmentReader.NormalizeText(cells[2])),
            IPAddress.Parse(HtmlFragmentReader.NormalizeText(cells[3])),
            ParseInt(HtmlFragmentReader.NormalizeText(cells[4])),
            ParseUnsignedLong(HtmlFragmentReader.NormalizeText(cells[5])));
    }

    private static DateTime ParseDateTime(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture);
    }

    private static TimeSpan ParseTimeSpan(string value)
    {
        if (TimeSpan.TryParseExact(value, ["hh\\:mm\\:ss", "h\\:m\\:s"], CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
    }

    private static int ParseInt(string value)
    {
        return int.Parse(CleanNumeric(value), CultureInfo.InvariantCulture);
    }

    private static long ParseLong(string value)
    {
        return long.Parse(CleanNumeric(value), CultureInfo.InvariantCulture);
    }

    private static ulong ParseUnsignedLong(string value)
    {
        return ulong.Parse(CleanNumeric(value), CultureInfo.InvariantCulture);
    }

    private static string CleanNumeric(string value)
    {
        return value.Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    [GeneratedRegex(@"Показано\s+с\s+(?<start>\d+)\s+по\s+(?<end>\d+)\s+из\s+(?<total>\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MetaInfoRegex();

    [GeneratedRegex(@"Данные\s+от:\s*(?<from>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s+до\s*(?<to>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AdminPeriodRegex();

    [GeneratedRegex(@"Данные\s+за:\s*(?<date>\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TopDateRegex();
}
