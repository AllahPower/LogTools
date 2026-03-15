using System.Text.RegularExpressions;
using LogsParser.Infrastructure;
using LogsParser.Models;

namespace LogsParser.Parsing;

public static partial class LogsFilterCatalogParser
{
    public static LogsFilterCatalog Parse(string html)
    {
        ThrowHelper.ThrowIfNullOrWhiteSpace(html);

        var typeSelect = ExtractTypeSelect(html);
        var filterOptions = ParseFilterOptions(typeSelect);
        var parametersByFilter = ParseAdditionalParameters(html);
        var account = LogsAccountParser.Parse(html);

        var filters = filterOptions
            .Select(option => new LogsFilterDefinition(
                option.Code,
                option.Name,
                parametersByFilter.TryGetValue(option.Code, out var parameters)
                    ? parameters
                    : Array.Empty<LogsFilterAdditionalParameter>()))
            .ToArray();

        return new LogsFilterCatalog(filters, account);
    }

    private static string ExtractTypeSelect(string html)
    {
        var selectMatch = TypeSelectRegex().Match(html);
        if (!selectMatch.Success)
        {
            return string.Empty;
        }

        return selectMatch.Groups["content"].Value;
    }

    private static IReadOnlyList<(string Code, string Name)> ParseFilterOptions(string selectHtml)
    {
        if (string.IsNullOrWhiteSpace(selectHtml))
        {
            return Array.Empty<(string Code, string Name)>();
        }

        return OptionRegex().Matches(selectHtml)
            .Select(static match => (
                Code: HtmlFragmentReader.NormalizeText(match.Groups["value"].Value),
                Name: HtmlFragmentReader.NormalizeText(match.Groups["label"].Value)))
            .Where(static option => !string.IsNullOrWhiteSpace(option.Code))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<LogsFilterAdditionalParameter>> ParseAdditionalParameters(string html)
    {
        var result = new Dictionary<string, List<LogsFilterAdditionalParameter>>(StringComparer.Ordinal);

        foreach (Match match in DynamicParameterBlockRegex().Matches(html))
        {
            var filterCode = HtmlFragmentReader.NormalizeText(match.Groups["filter"].Value);
            var queryKey = HtmlFragmentReader.NormalizeText(match.Groups["name"].Value);
            var label = HtmlFragmentReader.NormalizeText(match.Groups["label"].Value);
            if (string.IsNullOrWhiteSpace(filterCode) || string.IsNullOrWhiteSpace(queryKey))
            {
                continue;
            }

            if (!result.TryGetValue(filterCode, out var parameters))
            {
                parameters = [];
                result[filterCode] = parameters;
            }

            parameters.Add(new LogsFilterAdditionalParameter(queryKey, label));
        }

        return result.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<LogsFilterAdditionalParameter>)pair.Value
                .DistinctBy(static parameter => parameter.QueryKey, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
    }

    [GeneratedRegex("""<select[^>]*name=["']type\[\]["'][^>]*>(?<content>.*?)</select>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TypeSelectRegex();

    [GeneratedRegex("""<option[^>]*value=["'](?<value>[^"']+)["'][^>]*>(?<label>.*?)</option>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OptionRegex();

    [GeneratedRegex("""<div[^>]*class=["'][^"']*js_component_filter_item[^"']*["'][^>]*data-filter-type=["'](?<filter>[^"']+)["'][^>]*>.*?<label[^>]*>(?<label>.*?)</label>.*?<(?:input|select|textarea)[^>]*name=["'](?<name>[^"']+)["'][^>]*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DynamicParameterBlockRegex();
}
