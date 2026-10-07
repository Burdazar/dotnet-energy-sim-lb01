using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using Battery.Battery;
using Battery.ConsoleUi;
using Battery.Persistence;
using Battery.Simulation.Meters;

namespace Battery.Simulation;

public sealed class BatterySimulator
{
    private enum DeviceSetupStage
    {
        None,
        DeviceCount,
        DevicePower
    }

    private readonly SimulationOptions _options;
    private readonly BatteryModel _battery;
    private readonly ConsoleDashboard _dashboard;
    private readonly List<DeviceLoad> _deviceLoads;
    private readonly List<IEnergyMeter> _meters;
    private readonly MeterReadingObservable _meterUpdates = new();
    private readonly SimulationHistory _history = new();
    private AutoScenarioPhase? _activeAutoScenarioPhase;
    private DateTime _currentSimulationTime;
    private ControlMode _mode = ControlMode.Manual;
    private double _manualTargetPowerKW;
    private bool _paused;
    private bool _quit;
    private bool _stepSizeChanged;
    private bool _pausedBeforeDeviceSetup;
    private DeviceSetupStage _deviceSetupStage;
    private int _devicesToConfigure;
    private int _nextDeviceNumber = 1;
    private string? _notice;

    public IObservable<MeterReading> MeterUpdates => _meterUpdates;
    public SimulationHistory History => _history;

    public BatterySimulator(SimulationOptions options)
    {
        options.Validate();
        _options = options;
        _currentSimulationTime = options.StartTime;
        _deviceLoads = AssignSocketNumbers(options.DeviceLoads);
        _battery = new BatteryModel(
            options.CapacityKWh,
            options.MaximumChargePowerKW,
            options.MaximumDischargePowerKW,
            options.InitialStateOfChargePercent);
        _meters =
        [
            new BatteryPowerMeter(_battery),
            new DeviceDemandMeter(() => TotalDeviceDemandKW),
            new NetPowerMeter(GetInflowPowerKW, () => TotalDeviceDemandKW)
        ];
        _dashboard = new ConsoleDashboard();
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var commands = Channel.CreateUnbounded<string>();
        var inputTask = Task.Run(async () =>
        {
            try
            {
                await _dashboard.ReadCommandsAsync(commands.Writer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                commands.Writer.TryComplete();
            }
        }, CancellationToken.None);

        try
        {
            await using var logger = new CsvSimulationLogger(_options.CsvPath);
            var elapsed = TimeSpan.Zero;
            var realTimeAccumulator = TimeSpan.Zero;
            var lastTimestamp = Stopwatch.GetTimestamp();
            var lastRenderTimestamp = lastTimestamp;
            var simulatedTime = _options.StartTime;
            var meterReadings = await ReadMetersAsync(simulatedTime, cancellationToken);
            _history.RecordStateOfCharge(simulatedTime, _battery.StateOfChargePercent);
            PublishMeterReadings(meterReadings);
            await logger.WriteAsync(simulatedTime, _battery, meterReadings, cancellationToken);
            RenderDashboard(simulatedTime, elapsed, _notice);

            using var pollTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
            while (elapsed < _options.Duration && !_quit && !cancellationToken.IsCancellationRequested)
            {
                ProcessPendingCommands(commands.Reader);
                if (_stepSizeChanged)
                {
                    realTimeAccumulator = TimeSpan.Zero;
                    _stepSizeChanged = false;
                }
                if (_quit || cancellationToken.IsCancellationRequested)
                    break;

                if (!await pollTimer.WaitForNextTickAsync(cancellationToken))
                    break;
                var now = Stopwatch.GetTimestamp();
                var wallElapsed = Stopwatch.GetElapsedTime(lastTimestamp, now);
                lastTimestamp = now;
                if (!_paused)
                    realTimeAccumulator += wallElapsed;

                ProcessPendingCommands(commands.Reader);
                if (_stepSizeChanged)
                {
                    realTimeAccumulator = TimeSpan.Zero;
                    _stepSizeChanged = false;
                }
                if (_quit || cancellationToken.IsCancellationRequested)
                    break;

                var stepped = false;
                while (!_paused && elapsed < _options.Duration)
                {
                    if (realTimeAccumulator < _options.RealTimeStepInterval)
                        break;

                    var step = TimeSpan.FromTicks(Math.Min(_options.StepSize.Ticks, (_options.Duration - elapsed).Ticks));
                    var stepTimestamp = _options.StartTime + elapsed;
                    _currentSimulationTime = stepTimestamp;
                    UpdateAutoScenario(stepTimestamp);
                    _battery.Advance(GetRequestedBatteryPowerKW(), step);
                    elapsed += step;
                    realTimeAccumulator -= _options.RealTimeStepInterval;
                    simulatedTime = _options.StartTime + elapsed;
                    _currentSimulationTime = simulatedTime;
                    _history.RecordStateOfCharge(simulatedTime, _battery.StateOfChargePercent);
                    meterReadings = await ReadMetersAsync(simulatedTime, cancellationToken);
                    PublishMeterReadings(meterReadings);
                    await logger.WriteAsync(simulatedTime, _battery, meterReadings, cancellationToken);
                    stepped = true;
                }

                var renderIntervalElapsed = Stopwatch.GetElapsedTime(lastRenderTimestamp, now);
                if (stepped || (!Console.IsOutputRedirected && renderIntervalElapsed >= TimeSpan.FromMilliseconds(100)))
                {
                    simulatedTime = _options.StartTime + elapsed;
                    RenderDashboard(simulatedTime, elapsed, _notice);
                    lastRenderTimestamp = now;
                }
            }

            simulatedTime = _options.StartTime + elapsed;
            _dashboard.Render(simulatedTime, elapsed, _options.Duration, _mode, _battery,
                GetTargetPower(), _options.StepSize, _options.RealTimeStepInterval, _deviceLoads.Count,
                TotalDeviceDemandKW, _paused, _history.GetRollingSummary(), _notice);
        }
        finally
        {
            commands.Writer.TryComplete();
            _meterUpdates.Dispose();
            _ = inputTask;
        }
    }

    private Task<MeterReading[]> ReadMetersAsync(DateTime timestamp, CancellationToken cancellationToken)
    {
        return Task.WhenAll(_meters.Select(meter => meter.ReadAsync(timestamp, cancellationToken)));
    }

    private void PublishMeterReadings(IEnumerable<MeterReading> readings)
    {
        foreach (var reading in readings)
            _meterUpdates.Publish(reading);
    }

    private void RenderDashboard(
        DateTime simulatedTime,
        TimeSpan elapsed,
        string? notice)
    {
        _dashboard.Render(simulatedTime, elapsed, _options.Duration, _mode, _battery,
            GetTargetPower(), _options.StepSize, _options.RealTimeStepInterval, _deviceLoads.Count,
            TotalDeviceDemandKW, _paused, _history.GetRollingSummary(), notice);
    }

    private double TotalDeviceDemandKW => _deviceLoads.Sum(device => device.PowerDemandKW);

    private void UpdateAutoScenario(DateTime simulatedTime)
    {
        if (_mode != ControlMode.Auto)
            return;

        var phase = AutoScenario.GetPhase(simulatedTime);
        if (ReferenceEquals(phase, _activeAutoScenarioPhase))
            return;

        _deviceLoads.Clear();
        _deviceLoads.AddRange(phase.Devices);
        _activeAutoScenarioPhase = phase;
        _notice = $"Auto scenario: {phase.Name}; {phase.Devices.Count} device(s); net load {phase.TargetPowerKW:+0.0;-0.0;0.0} kW";
    }

    private static List<DeviceLoad> AssignSocketNumbers(IEnumerable<DeviceLoad> deviceLoads)
    {
        var assignedLoads = deviceLoads.ToList();
        var occupiedSockets = assignedLoads
            .Where(device => device.SocketNumber > 0)
            .Select(device => device.SocketNumber)
            .ToHashSet();
        var nextSocketNumber = 1;

        for (var index = 0; index < assignedLoads.Count; index++)
        {
            if (assignedLoads[index].SocketNumber > 0)
                continue;

            while (!occupiedSockets.Add(nextSocketNumber))
                nextSocketNumber++;

            assignedLoads[index] = assignedLoads[index] with { SocketNumber = nextSocketNumber++ };
        }

        return assignedLoads;
    }

    private void BeginDeviceConfiguration(string dischargePowerArgument)
    {
        CancelDeviceConfiguration();
        _deviceLoads.Clear();
        _pausedBeforeDeviceSetup = _paused;
        _paused = true;
        _mode = ControlMode.Manual;
        _manualTargetPowerKW = string.IsNullOrWhiteSpace(dischargePowerArgument)
            ? 0
            : -ParsePower(dischargePowerArgument, 0);
        _deviceSetupStage = DeviceSetupStage.DeviceCount;
        _notice = "DISCHARGE: enter device count; press P to cancel and resume";
    }

    private void CancelDeviceConfiguration()
    {
        if (_deviceSetupStage == DeviceSetupStage.None)
            return;

        _deviceSetupStage = DeviceSetupStage.None;
        _devicesToConfigure = 0;
        _deviceLoads.Clear();
        _paused = _pausedBeforeDeviceSetup;
    }

    private void CompleteDeviceConfiguration()
    {
        _deviceSetupStage = DeviceSetupStage.None;
        _devicesToConfigure = 0;
        _paused = _pausedBeforeDeviceSetup;
        _notice = null;
    }

    private void DisconnectAllDevices()
    {
        CancelDeviceConfiguration();
        var disconnectedCount = _deviceLoads.Count;
        _deviceLoads.Clear();
        _notice = disconnectedCount == 0
            ? "No connected devices to unplug."
            : $"Unplugged all {disconnectedCount} connected device(s).";
    }

    private void ProcessDeviceSetupInput(string input)
    {
        if (_deviceSetupStage == DeviceSetupStage.DeviceCount)
        {
            if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
                count is < 0 or > SimulationOptions.MaximumDeviceCount)
            {
                _notice = $"Enter a whole number from 0 to {SimulationOptions.MaximumDeviceCount}, then press Enter";
                return;
            }

            _devicesToConfigure = count;
            _nextDeviceNumber = 1;
            if (count == 0)
            {
                CompleteDeviceConfiguration();
                _manualTargetPowerKW = 0;
                return;
            }

            _deviceSetupStage = DeviceSetupStage.DevicePower;
            _notice = $"Enter power demand in kW for device {_nextDeviceNumber}, then press Enter";
            return;
        }

        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var powerKW) ||
            !double.IsFinite(powerKW) || powerKW <= 0 || !double.IsFinite(TotalDeviceDemandKW + powerKW))
        {
            _notice = $"Enter a positive finite kW demand for device {_nextDeviceNumber}, then press Enter";
            return;
        }

        _deviceLoads.Add(new DeviceLoad($"Device {_nextDeviceNumber}", powerKW, _nextDeviceNumber));
        _devicesToConfigure--;
        _nextDeviceNumber++;
        if (_devicesToConfigure == 0)
        {
            CompleteDeviceConfiguration();
            return;
        }

        _notice = $"Enter power demand in kW for device {_nextDeviceNumber}, then press Enter";
    }

    private static bool IsControlCommand(string command)
    {
        return command is "quit" or "exit" or "toggle-pause" or "p" or "pause" or "resume" or "help" or
            "power-up" or "power-down" or "step-up" or "step-down" or "auto" or "manual" or
            "idle" or "fault" or "reset" or "discharge" or "disconnect" or "charge" or "step";
    }

    private double GetTargetPower()
    {
        if (_mode == ControlMode.Manual)
            return _manualTargetPowerKW;

        return AutoScenario.GetPhase(_currentSimulationTime).TargetPowerKW;
    }

    private double GetRequestedBatteryPowerKW()
    {
        return GetTargetPower() - TotalDeviceDemandKW;
    }

    private double GetInflowPowerKW() => Math.Max(0, GetTargetPower());

    private void ProcessPendingCommands(ChannelReader<string> reader)
    {
        while (reader.TryRead(out var line))
        {
            _notice = null;
            var separator = line.IndexOf(' ');
            var command = (separator < 0 ? line : line[..separator]).Trim().ToLowerInvariant();
            var argument = separator < 0 ? string.Empty : line[(separator + 1)..].Trim();

            if (_deviceSetupStage != DeviceSetupStage.None && !IsControlCommand(command))
            {
                ProcessDeviceSetupInput(line);
                continue;
            }

            try
            {
                switch (command)
                {
                    case "":
                        break;
                    case "help":
                        break;
                    case "toggle-pause":
                    case "p":
                        if (_deviceSetupStage != DeviceSetupStage.None)
                        {
                            CancelDeviceConfiguration();
                            _paused = false;
                            _notice = "Setup aborted and entries discarded";
                        }
                        else
                            _paused = !_paused;
                        break;
                    case "pause":
                        _paused = true;
                        break;
                    case "resume":
                        if (_deviceSetupStage == DeviceSetupStage.None)
                            _paused = false;
                        break;
                    case "auto":
                        CancelDeviceConfiguration();
                        _mode = ControlMode.Auto;
                        _activeAutoScenarioPhase = null;
                        UpdateAutoScenario(_currentSimulationTime);
                        break;
                    case "manual":
                        CancelDeviceConfiguration();
                        _mode = ControlMode.Manual;
                        _manualTargetPowerKW = 0;
                        _activeAutoScenarioPhase = null;
                        break;
                    case "charge":
                        CancelDeviceConfiguration();
                        _manualTargetPowerKW = ParsePower(argument, _battery.MaximumChargePowerKW);
                        _mode = ControlMode.Manual;
                        break;
                    case "power-up":
                    case "power-down":
                        AdjustPowerTarget(command == "power-up" ? 1 : -1);
                        break;
                    case "discharge":
                        BeginDeviceConfiguration(argument);
                        break;
                    case "disconnect":
                        DisconnectAllDevices();
                        break;
                    case "idle":
                        CancelDeviceConfiguration();
                        _mode = ControlMode.Manual;
                        _manualTargetPowerKW = 0;
                        _deviceLoads.Clear();
                        break;
                    case "fault":
                        _battery.InjectFault(string.IsNullOrWhiteSpace(argument) ? "Operator-injected fault" : argument);
                        break;
                    case "reset":
                        _battery.ResetFault();
                        break;
                    case "step":
                        var minutes = ParsePositiveNumber(argument, "step size");
                        if (minutes > _options.Duration.TotalMinutes)
                            throw new ArgumentOutOfRangeException(nameof(argument), "Step size cannot exceed the simulation duration.");
                        _options.StepSize = TimeSpan.FromMinutes(minutes);
                        _stepSizeChanged = true;
                        break;
                    case "step-up":
                    case "step-down":
                        AdjustStepSize(command == "step-up" ? 1 : -1);
                        break;
                    case "quit":
                    case "exit":
                        _quit = true;
                        break;
                    default:
                        _notice = $"Unknown command '{command}'. Type 'help' for commands.";
                        break;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
            {
                _notice = exception.Message;
            }
        }
    }

    private void AdjustPowerTarget(double adjustmentKW)
    {
        var currentTarget = GetTargetPower();
        _manualTargetPowerKW = Math.Clamp(
            currentTarget + adjustmentKW,
            -_battery.MaximumDischargePowerKW,
            _battery.MaximumChargePowerKW);
        _mode = ControlMode.Manual;
    }

    private void AdjustStepSize(double adjustmentMinutes)
    {
        var minimumMinutes = Math.Min(1, _options.Duration.TotalMinutes);
        var maximumMinutes = _options.Duration.TotalMinutes;
        var minutes = Math.Clamp(_options.StepSize.TotalMinutes + adjustmentMinutes, minimumMinutes, maximumMinutes);
        if (minutes != _options.StepSize.TotalMinutes)
        {
            _options.StepSize = TimeSpan.FromMinutes(minutes);
            _stepSizeChanged = true;
        }

    }

    private static double ParsePower(string argument, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return defaultValue;

        var power = ParsePositiveNumber(argument, "power");
        return power;
    }

    private static double ParsePositiveNumber(string argument, string name)
    {
        if (!double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value <= 0)
            throw new ArgumentException($"Provide a positive finite {name} value using invariant numeric format.");

        return value;
    }
}
