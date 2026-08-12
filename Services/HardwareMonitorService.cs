using System.Runtime.InteropServices;
using Autoseeder.Client.Models;
using LibreHardwareMonitor.Hardware;

namespace Autoseeder.Client.Services;

internal sealed class HardwareMonitorService : IVisitor, IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true
    };

    public HardwareMonitorService() => _computer.Open();

    public HardwareSnapshot Read()
    {
        _computer.Accept(this);

        var cpu = _computer.Hardware.FirstOrDefault(x => x.HardwareType == HardwareType.Cpu);
        var gpus = _computer.Hardware.Where(x =>
            x.HardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia).ToArray();
        var memory = _computer.Hardware.FirstOrDefault(x => x.HardwareType == HardwareType.Memory);

        var memoryUsage = ReadMemoryUsage();
        return new HardwareSnapshot(
            FindLoad(cpu, "CPU Total") ?? FindMax(cpu, SensorType.Load),
            FindTemperature(cpu, "CPU Package") ?? FindTemperature(cpu, "Core Average") ?? FindMax(cpu, SensorType.Temperature),
            FindMax(memory, SensorType.Load) ?? memoryUsage?.Load,
            memoryUsage?.UsedGigabytes,
            memoryUsage?.TotalGigabytes,
            gpus.Select(gpu => new GpuSnapshot(
                gpu.Name,
                FindLoad(gpu, "GPU Core") ?? FindLoad(gpu, "D3D 3D") ?? FindMax(gpu, SensorType.Load),
                FindTemperature(gpu, "GPU Core") ?? FindTemperature(gpu, "GPU Hot Spot") ??
                FindTemperature(gpu, "Core") ?? FindMax(gpu, SensorType.Temperature))).ToArray());
    }

    public void VisitComputer(IComputer computer) => computer.Traverse(this);

    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware)
            subHardware.Accept(this);
    }

    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }

    private static double? FindLoad(IHardware? hardware, string name) =>
        hardware?.Sensors.FirstOrDefault(x =>
            x.SensorType == SensorType.Load && x.Name.Contains(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static double? FindTemperature(IHardware? hardware, string name) =>
        AllSensors(hardware).FirstOrDefault(x =>
            x.SensorType == SensorType.Temperature && x.Name.Contains(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static double? FindMax(IHardware? hardware, SensorType type)
    {
        var values = AllSensors(hardware)
            .Where(x => x.SensorType == type && x.Value.HasValue)
            .Select(x => (double)x.Value!.Value)
            .ToArray();
        return values is { Length: > 0 } ? values.Max() : null;
    }

    private static IEnumerable<ISensor> AllSensors(IHardware? hardware)
    {
        if (hardware is null) return [];
        return hardware.Sensors.Concat(hardware.SubHardware.SelectMany(AllSensors));
    }

    private static (double Load, double UsedGigabytes, double TotalGigabytes)? ReadMemoryUsage()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) return null;
        const double bytesPerGigabyte = 1024d * 1024d * 1024d;
        var total = status.TotalPhysical / bytesPerGigabyte;
        var available = status.AvailablePhysical / bytesPerGigabyte;
        return (status.MemoryLoad, total - available, total);
    }

    public void Dispose()
    {
        _computer.Close();
        GC.SuppressFinalize(this);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
