namespace Battery.Simulation;

public sealed class SimulationOptions
{
    public const int MaximumDeviceCount = 4;
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromDays(1) - TimeSpan.FromSeconds(1);

    public double CapacityKWh { get; init; } = 200;
    public double MaximumChargePowerKW { get; init; } = 50;
    public double MaximumDischargePowerKW { get; init; } = 50;
    public double InitialStateOfChargePercent { get; init; } = 0;
    public TimeSpan StepSize { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan RealTimeStepInterval { get; init; } = TimeSpan.FromSeconds(1);
    public DateTime StartTime { get; init; } = DateTime.Today;
    public TimeSpan Duration { get; init; } = DefaultDuration;
    public string CsvPath { get; init; } = Path.Combine("data", "battery-run.csv");
    public IReadOnlyList<DeviceLoad> DeviceLoads { get; init; } = Array.Empty<DeviceLoad>();
    public double TotalDeviceDemandKW => DeviceLoads.Sum(device => device.PowerDemandKW);

    public void Validate()
    {
        if (!double.IsFinite(CapacityKWh) || CapacityKWh <= 0)
            throw new ArgumentOutOfRangeException(nameof(CapacityKWh), "Capacity must be positive and finite.");
        if (!double.IsFinite(MaximumChargePowerKW) || MaximumChargePowerKW <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumChargePowerKW), "Maximum charge power must be positive and finite.");
        if (!double.IsFinite(MaximumDischargePowerKW) || MaximumDischargePowerKW <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumDischargePowerKW), "Maximum discharge power must be positive and finite.");
        if (!double.IsFinite(InitialStateOfChargePercent) || InitialStateOfChargePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(InitialStateOfChargePercent), "Initial state of charge must be between 0 and 100 percent.");
        if (StepSize <= TimeSpan.Zero || Duration <= TimeSpan.Zero || RealTimeStepInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StepSize), "Step size, real-time step interval, and duration must be positive.");
        if (DeviceLoads is null)
            throw new ArgumentNullException(nameof(DeviceLoads));
        if (DeviceLoads.Count > MaximumDeviceCount)
            throw new ArgumentOutOfRangeException(nameof(DeviceLoads), $"At most {MaximumDeviceCount} devices may be connected.");
        var assignedSocketNumbers = new HashSet<int>();
        foreach (var device in DeviceLoads)
        {
            if (device is null)
                throw new ArgumentException("Device entries cannot be null.", nameof(DeviceLoads));
            if (!double.IsFinite(device.PowerDemandKW) || device.PowerDemandKW <= 0)
                throw new ArgumentOutOfRangeException(nameof(DeviceLoads), "Device power demand must be positive and finite.");
            if (device.SocketNumber < 0 ||
                (device.SocketNumber > 0 && !assignedSocketNumbers.Add(device.SocketNumber)))
                throw new ArgumentException("Specified device socket numbers must be positive and unique.", nameof(DeviceLoads));
        }
        if (!double.IsFinite(TotalDeviceDemandKW))
            throw new ArgumentOutOfRangeException(nameof(DeviceLoads), "Total device power demand must be finite.");
        if (string.IsNullOrWhiteSpace(CsvPath))
            throw new ArgumentException("A CSV output path is required.", nameof(CsvPath));
    }
}
