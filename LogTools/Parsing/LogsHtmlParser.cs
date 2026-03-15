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

        Logger.LogDebug("ParseLogs: parsing HTML ({ContentLength} chars)", html.Length);

        var account = LogsAccountParser.Parse(html);
        var tbody = HtmlFragmentReader.ExtractFirstTagInnerHtml(html, "tbody");
        if (string.IsNullOrWhiteSpace(tbody))
        {
            Logger.LogDebug("ParseLogs: no <tbody> found, returning empty result");
            return new LogsPage(Array.Empty<LogEntry>(), ParseMetaInfo(html), account);
        }

        var rows = HtmlFragmentReader.ExtractTableRows(tbody);
        var skipped = 0;
        var withSender = 0;
        var withTarget = 0;

        var entries = new List<LogEntry>(rows.Count);
        foreach (var row in rows)
        {
            var entry = ParseLogEntry(row);
            if (entry is null)
            {
                skipped++;
                continue;
            }

            entries.Add(entry);
            if (entry.Sender is not null) withSender++;
            if (entry.Target is not null) withTarget++;
        }

        var metaInfo = ParseMetaInfo(html);

        Logger.LogDebug(
            "ParseLogs: {EntryCount} entries parsed, {Skipped} rows skipped, {WithSender} with sender, {WithTarget} with target",
            entries.Count, skipped, withSender, withTarget);

        return new LogsPage(entries, metaInfo, account);
    }

    public static AdminActivityReport ParseAdminActivity(string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        Logger.LogDebug("ParseAdminActivity: parsing HTML ({ContentLength} chars)", html.Length);

        var periodMatch = AdminPeriodRegex().Match(WebUtility.HtmlDecode(html));
        if (!periodMatch.Success)
        {
            Logger.LogWarning("ParseAdminActivity: period not found in HTML");
            throw new HtmlParsingException("Admin activity period was not found.");
        }

        var period = (
            ParseDateTime(periodMatch.Groups["from"].Value),
            ParseDateTime(periodMatch.Groups["to"].Value));

        Logger.LogDebug("ParseAdminActivity: period {From} — {To}",
            period.Item1.ToString("yyyy-MM-dd"), period.Item2.ToString("yyyy-MM-dd"));

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

        Logger.LogDebug("ParseAdminActivity: {EntryCount} admins, {TotalReports} reports, {TotalBans} bans",
            entries.Length, metaInfo.TotalReports, metaInfo.TotalBans);

        return new AdminActivityReport(period, entries, metaInfo);
    }

    public static TopOperationsReport ParseTopOperations(string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        Logger.LogDebug("ParseTopOperations: parsing HTML ({ContentLength} chars)", html.Length);

        var dateMatch = TopDateRegex().Match(WebUtility.HtmlDecode(html));
        if (!dateMatch.Success)
        {
            Logger.LogWarning("ParseTopOperations: date not found in HTML");
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

        Logger.LogDebug("ParseTopOperations: date={Date}, {EntryCount} entries, {TotalTransactions} transactions",
            date, entries.Length, metaInfo.TotalTransactions);

        return new TopOperationsReport(date, entries, metaInfo);
    }

    private static LogEntry? ParseLogEntry(string rowHtml)
    {
        var cells = HtmlFragmentReader.ExtractTableCells(HtmlFragmentReader.ExtractInnerHtml(rowHtml), "td");
        if (cells.Count < 2)
        {
            Logger.LogTrace("ParseLogEntry: skipping row with {CellCount} cells (need at least 2)", cells.Count);
            return null;
        }

        var timestamp = ParseDateTime(HtmlFragmentReader.NormalizeText(cells[0]));
        var logHtml = HtmlFragmentReader.ExtractInnerHtml(cells[1]).Trim();
        var logText = HtmlFragmentReader.NormalizeText(logHtml);

        long? senderMoney = null, senderBank = null, senderDonate = null;
        long? targetMoney = null, targetBank = null, targetDonate = null;
        LogAdditionalInfo? senderInfo = null, targetInfo = null;

        if (cells.Count >= 3)
        {
            var dataHtml = cells[2];

            foreach (Match match in ParticipantDataRegex().Matches(dataHtml))
            {
                var marker = match.Groups["marker"].Value;
                var money = ParseLong(HtmlFragmentReader.NormalizeText(match.Groups["money"].Value));
                var bank = ParseLong(HtmlFragmentReader.NormalizeText(match.Groups["bank"].Value));
                var donate = ParseLong(HtmlFragmentReader.NormalizeText(match.Groups["donate"].Value));

                if (string.Equals(marker, "I", StringComparison.Ordinal))
                {
                    senderMoney = money;
                    senderBank = bank;
                    senderDonate = donate;
                }
                else if (string.Equals(marker, "II", StringComparison.Ordinal))
                {
                    targetMoney = money;
                    targetBank = bank;
                    targetDonate = donate;
                }
            }

            var hiddenBlocks = HtmlFragmentReader.ExtractElementsByClass(dataHtml, "app__hidden");
            if (hiddenBlocks.Count >= 1)
            {
                senderInfo = ParseAdditionalInfoBlock(hiddenBlocks[0]);
            }

            if (hiddenBlocks.Count >= 2)
            {
                targetInfo = ParseAdditionalInfoBlock(hiddenBlocks[1]);
            }
        }

        string? senderLastIp = null, senderRegIp = null;
        string? targetLastIp = null, targetRegIp = null;

        if (cells.Count >= 4)
        {
            foreach (var element in HtmlFragmentReader.ExtractElementsByClass(cells[3], "table-ip"))
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

                if (string.Equals(kind, "I:", StringComparison.OrdinalIgnoreCase))
                {
                    senderLastIp = lastIp;
                    senderRegIp = registrationIp;
                }
                else if (string.Equals(kind, "II:", StringComparison.OrdinalIgnoreCase))
                {
                    targetLastIp = lastIp;
                    targetRegIp = registrationIp;
                }
            }
        }

        var sender = senderMoney.HasValue || senderInfo is not null || senderLastIp is not null
            ? new LogParticipant(senderMoney, senderBank, senderDonate, senderInfo, senderLastIp, senderRegIp)
            : null;

        var target = targetMoney.HasValue || targetInfo is not null || targetLastIp is not null
            ? new LogParticipant(targetMoney, targetBank, targetDonate, targetInfo, targetLastIp, targetRegIp)
            : null;

        return new LogEntry(timestamp, logText, logHtml, sender, target);
    }

    private static LogAdditionalInfo? ParseAdditionalInfoBlock(string hiddenBlockHtml)
    {
        var liValues = HtmlFragmentReader.ExtractTopLevelBlocks(HtmlFragmentReader.ExtractInnerHtml(hiddenBlockHtml), "li")
            .SelectMany(static li =>
                HtmlFragmentReader.ExtractTopLevelBlocks(HtmlFragmentReader.ExtractInnerHtml(li), "code")
                    .Select(HtmlFragmentReader.ExtractInnerHtml)
                    .Select(HtmlFragmentReader.NormalizeText))
            .ToArray();

        if (liValues.Length < 10)
        {
            Logger.LogTrace("ParseAdditionalInfoBlock: skipping block with {ValueCount} values (need 10)", liValues.Length);
            return null;
        }

        return new LogAdditionalInfo(
            ParseLong(liValues[0]),
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

    private static LogPageMetaInfo? ParseMetaInfo(string html)
    {
        var match = MetaInfoRegex().Match(WebUtility.HtmlDecode(html));
        if (!match.Success)
        {
            Logger.LogTrace("ParseMetaInfo: pagination info not found in HTML");
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

    [GeneratedRegex(@"<code><strong>(?<marker>I{1,2}):</strong></code>\s*<code>(?<money>[^<]+)</code>\s*/\s*<code>(?<bank>[^<]+)</code>\s*/\s*<code>(?<donate>[^<]+)</code>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ParticipantDataRegex();
}
