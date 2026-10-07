using Battery.Simulation;

namespace Battery
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var options = new SimulationOptions
            {
                CapacityKWh = 200,
                MaximumChargePowerKW = 50,
                MaximumDischargePowerKW = 50,
                InitialStateOfChargePercent = 0,
                StepSize = TimeSpan.FromMinutes(15),
                StartTime = DateTime.Today,
                Duration = SimulationOptions.DefaultDuration,
                CsvPath = Path.Combine("data", "battery-run.csv")
            };

            using var cancellationTokenSource = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellationTokenSource.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
                await new BatterySimulator(options).RunAsync(cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Simulation cancelled.");
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }

    }
}
