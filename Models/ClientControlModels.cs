namespace Autoseeder.Client.Models;

public sealed record ClientTelemetryPoint(
    DateTime TimestampUtc,
    double? CpuLoad,
    double? CpuTemperature,
    double? MemoryLoad,
    double? MemoryUsedGigabytes,
    double? MemoryTotalGigabytes,
    IReadOnlyCollection<ClientGpuTelemetry> Gpus);

public sealed record ClientGpuTelemetry(
    string Name,
    double? Load,
    double? Temperature);

public sealed record ClientTelemetryReport(
    string DeviceId,
    string DeviceName,
    string ClientVersion,
    bool IsSeeding,
    bool IsSquadRunning,
    DateTime? ShutdownAtUtc,
    string? ScheduledAction,
    ClientTelemetryPoint Telemetry);

public sealed record ClientCommandMessage(
    string RequestId,
    string Command,
    DateTime? ShutdownAtUtc,
    string? ScheduledAction);

public sealed record ClientCommandAcknowledgement(
    string RequestId,
    string DeviceId,
    string Command,
    bool Succeeded,
    string? Error);
