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

    /// <summary>
    /// Same as <see cref="NormalizeText"/> but keeps line breaks, which carry meaning in
    /// revealed row values (each item of a set sits on its own line).
    /// </summary>
    public static string NormalizeMultilineText(string html)
    {
        var withoutTags = AnyTagRegex().Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var lines = decoded
            .Split('\n')
            .Select(static line => HorizontalWhitespaceRegex().Replace(line, " ").Trim());

        return BlankLineRunRegex().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    /// <summary>
    /// Reads an attribute off an element's opening tag. Probing the opening tag with a
    /// dedicated pattern keeps the result independent of attribute order, unlike a single
    /// combined pattern that would only match one arrangement.
    /// </summary>
    public static string? ExtractAttributeValue(string elementHtml, string attributeName)
    {
        ThrowHelper.ThrowIfNullOrWhiteSpace(attributeName);

        if (string.IsNullOrEmpty(elementHtml))
        {
            return null;
        }

        var openTagEnd = elementHtml.IndexOf('>');
        var openTag = openTagEnd < 0 ? elementHtml : elementHtml[..(openTagEnd + 1)];

        var match = AttributeRegex(attributeName).Match(openTag);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
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

    [GeneratedRegex(@"[^\S\n]+", RegexOptions.Singleline)]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.Singleline)]
    private static partial Regex BlankLineRunRegex();

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

    private static Regex AttributeRegex(string attributeName)
    {
        return new Regex(
            $@"(?<![-\w]){Regex.Escape(attributeName)}\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)')",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    private static Regex ElementByClassRegex(string className)
    {
        // The class must be bounded by whitespace or the quote itself. A \b boundary is not
        // enough: '-' is a non-word character, so \bapp__hidden\b also matches app__hidden-set.
        return new Regex(
            $@"<(?<tag>\w+)\b[^>]*class\s*=\s*(?<quote>[""'])(?:[^""']*\s)?{Regex.Escape(className)}(?:\s[^""']*)?\k<quote>[^>]*>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }
}
