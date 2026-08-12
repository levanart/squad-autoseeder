namespace Autoseeder.Client.Models;

public sealed record HardwareSnapshot(
    double? CpuLoad,
    double? CpuTemperature,
    double? MemoryLoad,
    double? MemoryUsedGigabytes,
    double? MemoryTotalGigabytes,
    IReadOnlyCollection<GpuSnapshot> Gpus);

public sealed record GpuSnapshot(
    string Name,
    double? Load,
    double? Temperature);
