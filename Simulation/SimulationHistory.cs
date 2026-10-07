using System.Collections.ObjectModel;

namespace Battery.Simulation;

public sealed record StateOfChargeSample(DateTime Timestamp, double Percent);

public sealed record StateOfChargeSummary(double AveragePercent, double MinimumPercent, double MaximumPercent);

public sealed record DailyStateOfChargeSummary(
    DateOnly Date,
    double TotalPercent,
    double AveragePercent,
    double MinimumPercent,
    double MaximumPercent);

public sealed class SimulationHistory
{
    public const int RollingReadingLimit = 96;

    private readonly Dictionary<DateTime, double> _stateOfChargeByTimestamp = [];
    private readonly Queue<StateOfChargeSample> _recentStateOfCharge = new();
    private readonly ReadOnlyDictionary<DateTime, double> _readOnlyStateOfChargeByTimestamp;

    public SimulationHistory()
    {
        _readOnlyStateOfChargeByTimestamp = new ReadOnlyDictionary<DateTime, double>(_stateOfChargeByTimestamp);
    }

    public IReadOnlyDictionary<DateTime, double> StateOfChargeByTimestamp => _readOnlyStateOfChargeByTimestamp;

    public IReadOnlyCollection<StateOfChargeSample> RecentStateOfCharge => _recentStateOfCharge.ToArray();

    public void RecordStateOfCharge(DateTime timestamp, double percent)
    {
        if (!double.IsFinite(percent))
            throw new ArgumentOutOfRangeException(nameof(percent), "State of charge must be finite.");

        _stateOfChargeByTimestamp[timestamp] = percent;
        _recentStateOfCharge.Enqueue(new StateOfChargeSample(timestamp, percent));
        while (_recentStateOfCharge.Count > RollingReadingLimit)
            _recentStateOfCharge.Dequeue();
    }

    public StateOfChargeSummary? GetRollingSummary()
    {
        var readings = _recentStateOfCharge.Select(sample => sample.Percent)
            .Where(double.IsFinite)
            .ToArray();
        if (readings.Length == 0)
            return null;

        return new StateOfChargeSummary(readings.Average(), readings.Min(), readings.Max());
    }

    public IReadOnlyList<DailyStateOfChargeSummary> GetDailySummaries(DateTime fromInclusive)
    {
        return _stateOfChargeByTimestamp
            .Where(entry => entry.Key >= fromInclusive)
            .GroupBy(entry => DateOnly.FromDateTime(entry.Key))
            .Select(group => new DailyStateOfChargeSummary(
                group.Key,
                group.Sum(entry => entry.Value),
                group.Average(entry => entry.Value),
                group.Min(entry => entry.Value),
                group.Max(entry => entry.Value)))
            .OrderBy(summary => summary.Date)
            .ToArray();
    }
}
