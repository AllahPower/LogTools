namespace LogsParser.Models;

public sealed record LogsPage(
    IReadOnlyList<LogEntry> Entries,
    LogPageMetaInfo? MetaInfo = null,
    LogsAccount? Account = null);

public sealed record LogEntry(
    DateTime Timestamp,
    string Text,
    string Html,
    LogParticipant? Sender = null,
    LogParticipant? Target = null);

public sealed record LogParticipant(
    long? Money = null,
    long? Bank = null,
    long? Donate = null,
    LogAdditionalInfo? AdditionalInfo = null,
    string? LastIp = null,
    string? RegistrationIp = null);

public sealed record LogAdditionalInfo(
    long AccountId,
    long VC,
    long SubAccount1,
    long SubAccount2,
    long SubAccount3,
    long SubAccount4,
    long SubAccount5,
    long SubAccount6,
    long Deposit,
    int AdminLevel);

public sealed record LogPageMetaInfo(int Start, int End, int Total);
