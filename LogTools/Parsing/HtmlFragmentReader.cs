using System.Net;
using System.Text.RegularExpressions;
using LogsParser.Infrastructure;

namespace LogsParser.Parsing;

internal static partial class HtmlFragmentReader
{
    public static string? ExtractFirstTagInnerHtml(string html, string tagName)
    {
        var outer = ExtractFirstOuterBlock(html, tagName);
        return outer is null ? null : ExtractInnerHtml(outer);
    }

    public static string? ExtractFirstOuterBlock(string html, string tagName)
    {
        var matches = TagRegex(tagName).Matches(html);
        var depth = 0;
        var startIndex = -1;

        foreach (Match match in matches)
        {
            var isClosing = match.Value.StartsWith("</", StringComparison.Ordinal);
            if (!isClosing)
            {
                if (depth == 0)
                {
                    startIndex = match.Index;
                }

                depth++;
                continue;
            }

            if (depth == 0)
            {
                continue;
            }

            depth--;
            if (depth == 0 && startIndex >= 0)
            {
                return html[startIndex..(match.Index + match.Length)];
            }
        }

        return null;
    }

    public static IReadOnlyList<string> ExtractTopLevelBlocks(string html, string tagName)
    {
        var matches = TagRegex(tagName).Matches(html);
        var blocks = new List<string>();
        var depth = 0;
        var startIndex = -1;

        foreach (Match match in matches)
        {
            var isClosing = match.Value.StartsWith("</", StringComparison.Ordinal);
            if (!isClosing)
            {
                if (depth == 0)
                {
                    startIndex = match.Index;
                }

                depth++;
                continue;
            }

            if (depth == 0)
            {
                continue;
            }

            depth--;
            if (depth == 0 && startIndex >= 0)
            {
                blocks.Add(html[startIndex..(match.Index + match.Length)]);
                startIndex = -1;
            }
        }

        return blocks;
    }

    public static IReadOnlyList<string> ExtractTableRows(string html)
    {
        return LooseTableRowRegex().Matches(html)
            .Select(static match => match.Value)
            .ToArray();
    }

    public static IReadOnlyList<string> ExtractTableCells(string html, string tagName)
    {
        ThrowHelper.ThrowIfNullOrWhiteSpace(tagName);

        return LooseTableCellRegex(tagName).Matches(html)
            .Select(static match => match.Value)
            .ToArray();
    }

    public static string? ExtractFirstElementByClass(string html, string className)
    {
        var match = ElementByClassRegex(className).Match(html);
        if (!match.Success)
        {
            return null;
        }

        return ExtractOuterBlockFromIndex(html, match.Groups["tag"].Value, match.Index);
    }

    public static IReadOnlyList<string> ExtractElementsByClass(string html, string className)
    {
        var matches = ElementByClassRegex(className).Matches(html);
        var blocks = new List<string>();

        foreach (Match match in matches)
        {
            var block = ExtractOuterBlockFromIndex(html, match.Groups["tag"].Value, match.Index);
            if (!string.IsNullOrWhiteSpace(block))
            {
                blocks.Add(block);
            }
        }

        return blocks;
    }

    public static string RemoveElementsByClass(string html, string className)
    {
        var blocks = ExtractElementsByClass(html, className);
        var result = html;
        foreach (var block in blocks)
        {
            result = result.Replace(block, string.Empty, StringComparison.Ordinal);
        }

        return result;
    }

    public static string ExtractInnerHtml(string outerHtml)
    {
        var openTagEnd = outerHtml.IndexOf('>');
        if (openTagEnd < 0)
        {
            return string.Empty;
        }

        var closeTagStart = outerHtml.LastIndexOf("</", StringComparison.OrdinalIgnoreCase);
        if (closeTagStart <= openTagEnd)
        {
            return string.Empty;
        }

        return outerHtml[(openTagEnd + 1)..closeTagStart];
    }

    public static string NormalizeText(string html)
    {
        var withoutTags = AnyTagRegex().Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return MultiWhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string? ExtractOuterBlockFromIndex(string html, string tagName, int startIndex)
    {
        var fragment = html[startIndex..];
        return ExtractFirstOuterBlock(fragment, tagName);
    }

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Singleline)]
    private static partial Regex MultiWhitespaceRegex();

    [GeneratedRegex(@"<tr\b[^>]*>.*?(?=<tr\b|</tbody>|</thead>|</table>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LooseTableRowRegex();

    private static Regex TagRegex(string tagName)
    {
        return new Regex($@"</?{Regex.Escape(tagName)}\b[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    private static Regex LooseTableCellRegex(string tagName)
    {
        return new Regex(
            $@"<{Regex.Escape(tagName)}\b[^>]*>.*?(?=<(?:td|th|tr)\b|</tr>|</tbody>|</thead>|</table>|$)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    private static Regex ElementByClassRegex(string className)
    {
        return new Regex(
            $@"<(?<tag>\w+)\b[^>]*class\s*=\s*[""'][^""']*\b{Regex.Escape(className)}\b[^""']*[""'][^>]*>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }
}
