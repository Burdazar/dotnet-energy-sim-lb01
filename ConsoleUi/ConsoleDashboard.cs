using System.Threading.Channels;
using Battery.Battery;
using Battery.Simulation;

namespace Battery.ConsoleUi;

public sealed class ConsoleDashboard
{
    private readonly object _inputLock = new();
    private string _inputBuffer = string.Empty;
    private string[]? _lastDashboard;
    private string? _lastRenderedInput;
    private int _lastWindowWidth;

    public async Task ReadCommandsAsync(ChannelWriter<string> writer, CancellationToken cancellationToken)
    {
        if (Console.IsInputRedirected)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await Console.In.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;
                await writer.WriteAsync(line, cancellationToken);
            }

            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            ConsoleKeyInfo key;
            try
            {
                key = Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (TryGetImmediateCommand(key, out var immediateCommand))
            {
                if (!writer.TryWrite(immediateCommand))
                    return;
                continue;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                string command;
                lock (_inputLock)
                {
                    command = _inputBuffer;
                    _inputBuffer = string.Empty;
                }

                if (!writer.TryWrite(command))
                    return;
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                lock (_inputLock)
                {
                    if (_inputBuffer.Length > 0)
                        _inputBuffer = _inputBuffer[..^1];
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                lock (_inputLock)
                    _inputBuffer += key.KeyChar;
            }
        }
    }

    private bool TryGetImmediateCommand(ConsoleKeyInfo key, out string command)
    {
        command = key.Key switch
        {
            ConsoleKey.UpArrow => "power-up",
            ConsoleKey.DownArrow => "power-down",
            ConsoleKey.Add or ConsoleKey.OemPlus => "step-up",
            ConsoleKey.Subtract or ConsoleKey.OemMinus => "step-down",
            ConsoleKey.A => "auto",
            ConsoleKey.M => "manual",
            ConsoleKey.D => "discharge",
            ConsoleKey.X => "disconnect",
            ConsoleKey.I => "idle",
            ConsoleKey.P => "toggle-pause",
            ConsoleKey.Q => "quit",
            ConsoleKey.C => "charge 20",
            ConsoleKey.F => "fault",
            ConsoleKey.R => "reset",
            ConsoleKey.H => "help",
            _ => string.Empty
        };

        if (command.Length == 0)
            return false;

        lock (_inputLock)
            return _inputBuffer.Length == 0;
    }

    public void Render(
        DateTime simulatedTime,
        TimeSpan elapsed,
        TimeSpan duration,
        ControlMode mode,
        BatteryModel battery,
        double targetPowerKW,
        TimeSpan stepSize,
        TimeSpan realTimeStepInterval,
        int deviceCount,
        double totalDeviceDemandKW,
        bool paused,
        StateOfChargeSummary? rollingSummary,
        string? notice = null)
    {
        var progress = duration.TotalSeconds <= 0 ? 0 : Math.Clamp(elapsed.TotalSeconds / duration.TotalSeconds, 0, 1);
        var inflowKW = Math.Max(0, targetPowerKW);
        var outflowKW = Math.Max(0, totalDeviceDemandKW);
        var totalFlowKW = inflowKW - outflowKW;
        var dashboardLines = new List<string>
        {
            "BATTERY ENERGY STORAGE SIMULATOR",
            "════════════════════════════════════════════════════════════",
            "SIMULATION",
            $"  Time       {simulatedTime:yyyy-MM-dd HH:mm}",
            $"  Progress   {progress:P0}     Mode: {mode}{(paused ? " (Paused)" : string.Empty)}",
            $"  Step       {stepSize.TotalMinutes:g} min simulated / {realTimeStepInterval.TotalSeconds:g} sec real",
            $"  Net   {targetPowerKW:+0.00;-0.00;0.00} kW     Devices: {deviceCount} ({totalDeviceDemandKW:F2} kW load)",
        };

        dashboardLines.AddRange(
        [
            "BATTERY",
            $"  State      {battery.State}     Fault: {battery.FaultMessage ?? "None"}",
            $"  Charge     [{ProgressBar(battery.StateOfChargePercent)}] {battery.StateOfChargePercent,6:F2}%  ({battery.StateOfChargeKWh:F2} / {battery.CapacityKWh:F2} kWh)",
            $"  Power  {battery.CurrentPowerKW,+8:F2} kW",
            $"  Throughput +{battery.ChargedEnergyKWh:F2} kWh charged / -{battery.DischargedEnergyKWh:F2} kWh discharged",
            "ENERGY METERS",
            $"  Inflow {inflowKW,+8:F2} kW",
            $"  Outflow{outflowKW,8:F2} kW",
            $"  Total  {totalFlowKW,+8:F2} kW  (inflow - outflow)",
            "RECENT HISTORY",
            rollingSummary is null
                ? "  SOC (last 96)  No readings"
                : $"  SOC (last 96)  Avg {rollingSummary.AveragePercent:F2}%  Min {rollingSummary.MinimumPercent:F2}%  Max {rollingSummary.MaximumPercent:F2}%",
            "OUTPUT",
            $"  CSV        {Path.Combine("data", "battery-run.csv")}",
            "COMMAND KEYS",
            $"  {KeyBinding("A", "Auto schedule")}  {KeyBinding("M", "Manual mode")}  {KeyBinding("D", "Configure discharge")}",
            $"  {KeyBinding("X", "Disconnect all")}  {KeyBinding("C", "Charge 20 kW")}  {KeyBinding("I", "Idle")}",
            $"  {KeyBinding("P", "Pause / resume")}  {KeyBinding("˄/˅", "Adjust power")}  {KeyBinding("+/-", "Adjust step")}",
            $"  {KeyBinding("Q", "Quit")}  {KeyBinding("F", "Inject fault")}  {KeyBinding("R", "Reset fault")}",
            "  H  Help",
            $"  After D: enter 0-{SimulationOptions.MaximumDeviceCount} devices, then each device's kW demand. P cancels and resumes.",
            "  X immediately unplugs all connected devices.",
            $"NOTICE  {notice ?? "Operating"}"
        ]);
        var fullDashboard = dashboardLines.ToArray();
        var statusLine = $"{simulatedTime:yyyy-MM-dd HH:mm:ss} {progress:P0} {mode}{(paused ? " (Paused)" : string.Empty)} | SOC {battery.StateOfChargePercent:F2}% | Battery {battery.CurrentPowerKW:+0.00;-0.00;0.00} kW | In {inflowKW:F2} / Out {outflowKW:F2} / Net {totalFlowKW:F2} kW";

        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            Console.WriteLine(statusLine);
            return;
        }

        try
        {
            var width = Math.Max(1, Console.WindowWidth);
            var displayWidth = Math.Max(1, width - 1);
            var dashboard = fullDashboard;
            var redrawAll = _lastDashboard is null || _lastWindowWidth != width ||
                _lastDashboard.Length != dashboard.Length;

            for (var row = 0; row < dashboard.Length; row++)
            {
                if (!redrawAll && string.Equals(_lastDashboard![row], dashboard[row], StringComparison.Ordinal))
                    continue;

                Console.SetCursorPosition(0, row);
                Console.Write(FitToWidth(dashboard[row], displayWidth));
            }

            string input;
            lock (_inputLock)
                input = _inputBuffer;
            if (redrawAll || !string.Equals(_lastRenderedInput, input, StringComparison.Ordinal))
            {
                Console.SetCursorPosition(0, dashboard.Length);
                Console.Write(FitToWidth($"> {input}", displayWidth));
                Console.SetCursorPosition(Math.Min(width - 1, input.Length + 2), dashboard.Length);
            }

            _lastDashboard = dashboard;
            _lastRenderedInput = input;
            _lastWindowWidth = width;
        }
        catch (IOException)
        {
            WriteFallbackStatusLine(statusLine);
        }
        catch (ArgumentOutOfRangeException)
        {
            WriteFallbackStatusLine(statusLine);
        }
        catch (InvalidOperationException)
        {
            WriteFallbackStatusLine(statusLine);
        }
    }

    private static void WriteFallbackStatusLine(string statusLine)
    {
        var width = Math.Max(1, Console.WindowWidth);
        Console.Write('\r');
        Console.Write(FitToWidth(statusLine, Math.Max(1, width - 1)));
    }

    private static string KeyBinding(string key, string description)
    {
        return $"{key,-4} {description,-20}";
    }

    private static string ProgressBar(double percent)
    {
        const int width = 20;
        var filled = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100 * width);
        return new string('■', filled) + new string('□', width - filled);
    }

    private static string FitToWidth(string value, int width)
    {
        return value.Length >= width ? value[..width] : value.PadRight(width);
    }
}
