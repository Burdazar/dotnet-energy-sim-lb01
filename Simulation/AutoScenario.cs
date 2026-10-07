namespace Battery.Simulation;

public sealed record AutoScenarioPhase(
    string Name,
    TimeOnly StartTime,
    double TargetPowerKW,
    IReadOnlyList<DeviceLoad> Devices);

public static class AutoScenario
{
    private static readonly AutoScenarioPhase[] Phases =
    [
        new("Overnight charge", new TimeOnly(0, 0), 20, []),
        new("Morning device supply", new TimeOnly(5, 0), 0,
        [
            new DeviceLoad("Water heater", 1.8, 1),
            new DeviceLoad("Kitchen appliances", 0.9, 2),
            new DeviceLoad("Lighting", 0.5, 3)
        ]),
        new("Daytime charge", new TimeOnly(9, 0), 20,
        [
            new DeviceLoad("Refrigerator", 0.4, 1),
            new DeviceLoad("Office equipment", 1.2, 2)
        ]),
        new("Evening device supply", new TimeOnly(16, 0), 0,
        [
            new DeviceLoad("Cooker", 2.5, 1),
            new DeviceLoad("Laundry", 1.8, 2),
            new DeviceLoad("Lighting", 0.8, 3),
            new DeviceLoad("Entertainment", 0.5, 4)
        ]),
        new("Night charge", new TimeOnly(21, 0), 20,
        [
            new DeviceLoad("Network equipment", 0.6, 1)
        ])
    ];

    public static AutoScenarioPhase GetPhase(DateTime simulatedTime)
    {
        var timeOfDay = TimeOnly.FromDateTime(simulatedTime);
        return Phases.Last(phase => phase.StartTime <= timeOfDay);
    }
}
