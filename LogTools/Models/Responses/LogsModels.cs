namespace LogsParser.Models;

public sealed record LogsPage(
    IReadOnlyList<LogEntry> Entries,
    LogPageMetaInfo? MetaInfo = null,
    LogsAccount? Account = null);

public sealed record LogEntry(
    DateTime Timestamp,
    string Text,
    string Html,
    int? Money = null,
    int? Bank = null,
    int? Donate = null,
    LogAdditionalInfo? AdditionalInfo = null,
    LogParticipant? Target = null,
    LogParticipant? Sender = null);

public sealed record LogAdditionalInfo(
    int HouseId,
    long HouseCash,
    long BusinessCash,
    long WarehouseCash,
    long FamilyCash,
    long PersonalVehiclesCost,
    long BusinessVehiclesCost,
    long OtherAssetsValue,
    long Deposits,
    int Reputation);

public sealed record LogParticipant(string LastIp, string RegistrationIp);

public sealed record LogPageMetaInfo(int Start, int End, int Total);
