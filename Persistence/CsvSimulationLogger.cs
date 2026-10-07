using System.Globalization;
using System.Text;
using Battery.Battery;
using Battery.Simulation.Meters;

namespace Battery.Persistence;

public sealed class CsvSimulationLogger : IAsyncDisposable
{
    private readonly StreamWriter _writer;

    public CsvSimulationLogger(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _writer = new StreamWriter(fullPath, append: false);
        _writer.WriteLine("date,time,soc_percent,soc_kwh,power_kw,state,charged_kwh,discharged_kwh,error,battery_meter_kw,device_demand_kw,net_power_kw");
    }

    public async Task WriteAsync(
        DateTime timestamp,
        BatteryModel battery,
        IReadOnlyCollection<MeterReading> meterReadings,
        CancellationToken cancellationToken = default)
    {
        var readingsByType = meterReadings.ToDictionary(reading => reading.MeterType, reading => reading.Value);
        var values = new[]
        {
            timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            battery.StateOfChargePercent.ToString("F2", CultureInfo.InvariantCulture),
            battery.StateOfChargeKWh.ToString("F2", CultureInfo.InvariantCulture),
            battery.CurrentPowerKW.ToString("F2", CultureInfo.InvariantCulture),
            battery.State.ToString(),
            battery.ChargedEnergyKWh.ToString("F2", CultureInfo.InvariantCulture),
            battery.DischargedEnergyKWh.ToString("F2", CultureInfo.InvariantCulture),
            Escape(battery.FaultMessage ?? string.Empty),
            readingsByType[MeterType.BatteryPower].ToString("F2", CultureInfo.InvariantCulture),
            readingsByType[MeterType.DeviceDemand].ToString("F2", CultureInfo.InvariantCulture),
            readingsByType[MeterType.NetPower].ToString("F2", CultureInfo.InvariantCulture)
        };

        var line = new StringBuilder();
        foreach (var value in values)
        {
            if (line.Length > 0)
                line.Append(',');
            line.Append(value);
        }

        await _writer.WriteLineAsync(line.ToString().AsMemory(), cancellationToken);
        await _writer.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
    }

    private static string Escape(string value)
    {
        return value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
