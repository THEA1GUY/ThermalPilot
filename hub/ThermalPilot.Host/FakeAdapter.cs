using ThermalPilot.Contracts;

namespace ThermalPilot.Host;

public sealed class FakeAdapter
{
    public DeviceCapabilities Capabilities { get; } = new(AdapterMode.Fake, false, false, false, ["simulateProfileActivation"]);

    public TelemetrySnapshot Snapshot() => new(AdapterMode.Fake, true,
        new SensorReading(null, "celsius", MeasurementQuality.Unavailable, null),
        new SensorReading(null, "celsius", MeasurementQuality.Unavailable, null));

    public CommandRecord Simulate(SimulateCommandRequest request, Guid commandId, DateTimeOffset now) =>
        new(commandId, request.IdempotencyKey, request.CorrelationId, request.ProfileId, request.ExpectedProfileVersion,
            CommandOutcome.Simulated, AdapterMode.Fake, true, false, now);
}
