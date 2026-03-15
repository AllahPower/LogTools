using System.Globalization;
using System.Text.RegularExpressions;
using LogsParser.Infrastructure;
using LogsParser.Models;

namespace LogsParser.Parsing;

internal static partial class LogsAccountParser
{
    public static LogsAccount? Parse(string html)
    {
        ThrowHelper.ThrowIfNullOrWhiteSpace(html);

        var nickname = ParseNickname(html);
        var badges = ParseBadges(html);
        var servers = ParseServers(html);

        if (string.IsNullOrWhiteSpace(nickname) && badges.Count == 0 && servers.Count == 0)
        {
            return null;
        }

        return new LogsAccount(nickname, badges, servers);
    }

    private static string ParseNickname(string html)
    {
        var match = NicknameRegex().Match(html);
        return match.Success
            ? HtmlFragmentReader.NormalizeText(match.Groups["nickname"].Value)
            : string.Empty;
    }

    private static IReadOnlyList<LogsAccountBadge> ParseBadges(string html)
    {
        var match = NavbarRightRegex().Match(html);
        if (!match.Success)
        {
            return Array.Empty<LogsAccountBadge>();
        }

        return BadgeRegex().Matches(match.Groups["content"].Value)
            .Select(static badge => HtmlFragmentReader.NormalizeText(badge.Groups["name"].Value))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Select(static value => new LogsAccountBadge(value))
            .ToArray();
    }

    private static IReadOnlyList<LogsAccountServer> ParseServers(string html)
    {
        var selectMatch = ServerSelectRegex().Match(html);
        if (!selectMatch.Success)
        {
            return Array.Empty<LogsAccountServer>();
        }

        return ServerOptionRegex().Matches(selectMatch.Groups["content"].Value)
            .Select(ParseServer)
            .Where(static server => server is not null)
            .Cast<LogsAccountServer>()
            .ToArray();
    }

    private static LogsAccountServer? ParseServer(Match match)
    {
        var value = HtmlFragmentReader.NormalizeText(match.Groups["value"].Value);
        var displayName = HtmlFragmentReader.NormalizeText(match.Groups["label"].Value);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        var name = displayName;
        var serverNameMatch = ServerDisplayNameRegex().Match(displayName);
        if (serverNameMatch.Success)
        {
            name = HtmlFragmentReader.NormalizeText(serverNameMatch.Groups["name"].Value);
        }

        return new LogsAccountServer(
            id,
            name,
            displayName,
            match.Groups["selected"].Success);
    }

    [GeneratedRegex("""<ul[^>]*class=["'][^"']*\bnavbar-nav\b[^"']*\bms-auto\b[^"']*["'][^>]*>(?<content>.*?)</ul>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NavbarRightRegex();

    [GeneratedRegex("""<span[^>]*class=["'][^"']*\bbadge\b[^"']*["'][^>]*>(?<name>.*?)</span>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BadgeRegex();

    [GeneratedRegex("""<a[^>]*id=["']navbarDropdown["'][^>]*>(?<nickname>.*?)</a>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NicknameRegex();

    [GeneratedRegex("""<select[^>]*name=["']server_number["'][^>]*>(?<content>.*?)</select>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ServerSelectRegex();

    [GeneratedRegex("""<option[^>]*value=["'](?<value>\d+)["'][^>]*(?<selected>\sselected(?:=["'][^"']*["'])?)?[^>]*>(?<label>.*?)</option>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ServerOptionRegex();

    [GeneratedRegex(@"^\[\d+\]\s*(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ServerDisplayNameRegex();
}
