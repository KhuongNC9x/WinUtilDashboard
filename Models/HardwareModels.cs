namespace WinUtilDashboard.Models;

public record CpuInfo(
    string Name,
    int Cores,
    int LogicalProcessors,
    int MaxClockMhz);

public record RamModule(
    double SizeGb,
    int SpeedMhz,
    string Manufacturer,
    string PartNumber);

public record MainboardInfo(
    string Manufacturer,
    string Product,
    string Version);

public record BiosInfo(
    string Manufacturer,
    string Name,
    string Version,
    string ReleaseDate);

public record ComputerSystemInfo(
    string Manufacturer,
    string Model,
    string SystemType);

public record GpuInfo(
    string Name,
    double VramGb,
    string DriverVersion);

public record PhysicalDiskInfo(
    string Model,
    double SizeGb,
    string MediaType);

public record LogicalDiskInfo(
    string DeviceId,
    string FileSystem,
    double SizeGb,
    double FreeGb);

public record HardwareReport(
    CpuInfo? Cpu,
    IReadOnlyList<RamModule> RamModules,
    MainboardInfo? Mainboard,
    BiosInfo? Bios,
    ComputerSystemInfo? ComputerSystem,
    IReadOnlyList<GpuInfo> Gpus,
    IReadOnlyList<PhysicalDiskInfo> PhysicalDisks,
    IReadOnlyList<LogicalDiskInfo> LogicalDisks);
