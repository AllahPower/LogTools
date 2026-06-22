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
        // Parse the value and the `selected` flag from the option's attributes independently so the
        // result does not depend on attribute order (a greedy combined pattern would swallow
        // `selected` whenever it followed `value`, leaving IsSelected always false).
        var attributes = match.Groups["attrs"].Value;

        var valueMatch = OptionValueRegex().Match(attributes);
        if (!valueMatch.Success ||
            !int.TryParse(valueMatch.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        var displayName = HtmlFragmentReader.NormalizeText(match.Groups["label"].Value);
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
            OptionSelectedRegex().IsMatch(attributes));
    }

    [GeneratedRegex("""<ul[^>]*class=["'][^"']*\bnavbar-nav\b[^"']*\bms-auto\b[^"']*["'][^>]*>(?<content>.*?)</ul>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NavbarRightRegex();

    [GeneratedRegex("""<span[^>]*class=["'][^"']*\bbadge\b[^"']*["'][^>]*>(?<name>.*?)</span>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BadgeRegex();

    [GeneratedRegex("""<a[^>]*id=["']navbarDropdown["'][^>]*>(?<nickname>.*?)</a>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NicknameRegex();

    [GeneratedRegex("""<select[^>]*name=["']server_number["'][^>]*>(?<content>.*?)</select>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ServerSelectRegex();

    [GeneratedRegex("""<option(?<attrs>[^>]*)>(?<label>.*?)</option>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ServerOptionRegex();

    [GeneratedRegex("""value=["'](?<value>\d+)["']""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OptionValueRegex();

    [GeneratedRegex(@"\bselected\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OptionSelectedRegex();

    [GeneratedRegex(@"^\[\d+\]\s*(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ServerDisplayNameRegex();
}
