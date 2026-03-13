namespace LogsParser.Models;

public sealed record LogsFilterCatalog(
    IReadOnlyList<LogsFilterDefinition> Filters,
    LogsAccount? Account = null);

public sealed record LogsFilterDefinition(
    string Code,
    string Name,
    IReadOnlyList<LogsFilterAdditionalParameter> AdditionalParameters);

public sealed record LogsFilterAdditionalParameter(
    string QueryKey,
    string Label,
    string? Placeholder = null);
