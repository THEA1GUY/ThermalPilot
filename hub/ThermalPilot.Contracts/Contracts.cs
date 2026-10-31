using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThermalPilot.Contracts;

public enum AdapterMode { Fake }
public enum ProfileMode { Silent, Balanced, Turbo }
public enum CommandOutcome { Simulated }
public enum MeasurementQuality { Unavailable, Synthetic, Fresh, Stale, Unsupported }
public enum EventKind { ProfileCreated, CommandSimulated, SyntheticReportCreated }

public sealed record SensorReading(double? Value, string Unit, MeasurementQuality Quality, DateTimeOffset? SampledAtUtc);
public sealed record TelemetrySnapshot(AdapterMode Adapter, bool IsSynthetic, SensorReading CpuTemperature, SensorReading GpuTemperature);
public sealed record DeviceCapabilities(AdapterMode Adapter, bool HardwareConnected, bool HardwareWritesEnabled, bool LiveSensorsAvailable, string[] SupportedCommands);
public sealed record HostStatus(int ContractVersion, int SchemaVersion, AdapterMode Adapter, bool IsSynthetic, bool CloudEnabled, bool CollectionEnabled, bool PhoneAccessEnabled);
public sealed record RetentionPolicy(bool CollectionEnabled, int RawHours, int AggregateDays, int ReportDays, int TelemetryBudgetMiB, bool CloudEnabled, decimal CloudBudget);

public sealed record CreateProfileRequest(string Name, ProfileMode BaseMode);
public sealed record ProfileRecord(Guid Id, string Name, ProfileMode BaseMode, int Version, DateTimeOffset CreatedAtUtc);
public sealed record SimulateCommandRequest(Guid ProfileId, int ExpectedProfileVersion, string IdempotencyKey, Guid CorrelationId, DateTimeOffset ExpiresAtUtc);
public sealed record CommandRecord(Guid Id, string IdempotencyKey, Guid CorrelationId, Guid ProfileId, int ProfileVersion, CommandOutcome Outcome, AdapterMode Adapter, bool IsSynthetic, bool HardwareChanged, DateTimeOffset CreatedAtUtc);
public sealed record CommandReceipt(CommandRecord Command, bool Replayed);
public sealed record CreateSyntheticReportRequest(string Title, string Body);
public sealed record ReportRecord(Guid Id, string Title, string Body, bool IsSynthetic, AdapterMode Adapter, DateTimeOffset CreatedAtUtc);
public sealed record AuditEvent(long Sequence, Guid CorrelationId, EventKind Kind, string Actor, Guid EntityId, DateTimeOffset CreatedAtUtc);
public sealed record StoreSummary(int SchemaVersion, long Profiles, long Commands, long Reports, long Events);

public static class ContractJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    public static JsonSerializerOptions Options { get; } = CreateOptions();
}
