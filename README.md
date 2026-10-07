# Battery Energy Storage Simulator

A .NET 10 console application that simulates a battery energy storage system, connected device loads, and a repeating daily automatic schedule. It displays battery and energy-flow metrics and writes simulation samples to CSV.

## Prerequisites

- .NET 10
- A terminal that supports interactive keyboard input for live controls

## Build and run

From the repository root:

```powershell
dotnet build Battery.csproj
dotnet run --project Battery.csproj
```

The program starts in **Manual** mode with no devices connected. Press **A** to start the automatic schedule, or use the manual controls below. Press **Ctrl+C** to cancel the simulation.

Each 15-minute simulation step takes one real-time second by default. A default daily run therefore takes about 96 seconds, plus startup and output time.

## Controls

| Key or command | Action |
| --- | --- |
| `A` | Enter Auto mode. The active automatic device profile replaces the current list. |
| `M` | Enter Manual mode with a zero power target. Current devices remain connected. |
| `D` / `discharge` | Pause and configure a device count (0–4), followed by each device's power demand in kW. |
| `X` / `unplug all` | Disconnect all devices. |
| `C` / `charge [kW]` | Set a positive manual charging target. `C` is the `charge 20` shortcut. |
| `I` / `idle` | Set the manual target to zero and clear connected devices. |
| `P` | Pause or resume. During discharge setup, cancels and discards that setup; during device selection, cancels without disconnecting devices. |
| Up/Down arrows | Adjust the manual target by 1 kW. |
| `+` / `-` | Increase or decrease the simulated step by one minute. |
| `F` / `R` | Inject a battery fault / reset the fault. |
| `Q` | Quit the simulation. |

During device setup, device names are assigned automatically (`Device 1`, etc.) and socket numbers are assigned from 1 to 4. For disconnection, enter a device name, a socket number (for example, `2` or `socket 2`), multiple comma-separated selections, or `all`.

## Automatic daily scenario

Auto mode repeats this schedule according to simulated time. Device demand is included in the battery request: `requested battery power = charging − device demand`.

| Simulated time | Charging | Devices | Device demand | Total Load |
| --- | ---: | ---: | ---: | ---: |
| 00:00–05:00 | +20 kW | 0 | 0.0 kW | 20.0 kW |
| 05:00–09:00 | 0 kW | 3 | 3.2 kW | 3.2 kW |
| 09:00–16:00 | +20 kW | 2 | 1.6 kW | 18.4 kW |
| 16:00–21:00 | 0 kW | 4 | 5.6 kW | 5.6 kW |
| 21:00–24:00 | +20 kW | 1 | 0.6 kW | 19.4 kW |

For example, in a charging phase with a 20 kW target and 1.6 kW of device demand, the battery receives an 18.4 kW charge request. In a zero-target phase, the battery is asked to discharge only enough to meet device demand. Actual battery power may be lower due to capacity, power limits, charge taper, or a fault.

## Model and displayed metrics

- Battery capacity and power are expressed in **kWh** and **kW**. Positive battery power charges the battery; negative power discharges it.
- Each simulation step applies the requested power for its simulated duration. State of charge is bounded between 0% and 100%; charge power tapers above 80% SOC.
- **Inflow** is the positive target only. **Outflow** is total connected-device demand as a positive value. **Total** is `Inflow − Outflow`.
- The dashboard's **Power** value is actual signed battery power. It may differ from the target or Total when battery limits, SOC, or faults constrain operation.
- `SimulationHistory` keeps timestamped SOC data in memory and a rolling window of the latest 96 readings. The CSV logger separately persists the full battery and meter sample data.

## Default and example parameters

`Program.cs` currently uses these settings:

| Parameter | Default example |
| --- | ---: |
| Capacity | 200 kWh |
| Maximum charge power | 50 kW |
| Maximum discharge power | 50 kW |
| Initial state of charge | 0% |
| Simulated step | 15 minutes |
| Real time per step | 1 second |
| Start time | Today at 00:00 |
| Duration | 23:59:59 |
| Maximum connected devices | 4 |
| CSV output | `data/battery-run.csv` |

To try different settings, edit the `SimulationOptions` initializer in `Program.cs`. For example, a shorter run could use `Duration = TimeSpan.FromHours(2)` and `StepSize = TimeSpan.FromMinutes(5)`. `RealTimeStepInterval` controls wall-clock pacing independently of the simulated step size.

## Output

The simulator creates the output directory when needed and overwrites the configured CSV file at the start of each run. Rows are written for the initial state and each simulation step. Timestamps include seconds; with the default start time and duration, the final record is `23:59:59`.
