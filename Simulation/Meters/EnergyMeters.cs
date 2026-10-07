using Battery.Battery;

namespace Battery.Simulation.Meters;

public enum MeterType
{
    BatteryPower,
    DeviceDemand,
    NetPower
}

public sealed record MeterReading(DateTime Timestamp, MeterType MeterType, double Value);

public interface IEnergyMeter
{
    MeterType Type { get; }
    Task<MeterReading> ReadAsync(DateTime timestamp, CancellationToken cancellationToken = default);
}

public abstract class EnergyMeter(MeterType type) : IEnergyMeter
{
    public MeterType Type { get; init; } = type;

    public virtual Task<MeterReading> ReadAsync(DateTime timestamp, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MeterReading(timestamp, Type, ReadValue()));
    }

    protected abstract double ReadValue();
}

public sealed class BatteryPowerMeter(BatteryModel battery) : EnergyMeter(MeterType.BatteryPower)
{
    protected override double ReadValue() => battery.CurrentPowerKW;
}

public sealed class DeviceDemandMeter(Func<double> getDemandKW) : EnergyMeter(MeterType.DeviceDemand)
{
    protected override double ReadValue() => getDemandKW();
}

public sealed class NetPowerMeter(Func<double> getInflowKW, Func<double> getOutflowKW) : EnergyMeter(MeterType.NetPower)
{
    protected override double ReadValue() => getInflowKW() - getOutflowKW();
}
