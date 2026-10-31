using System.Buffers.Binary;
using System.Text.Json;

namespace ThermalPilot.Contracts;

public sealed record LiveReading(string Id, double? Value, string Unit, string Source,
    MeasurementQuality Quality, DateTimeOffset? SampledAtUtc, string? Reason);
public sealed record LiveSnapshot(int ContractVersion, AdapterMode Adapter, bool IsSynthetic,
    Guid SessionId, long Sequence, DateTimeOffset CapturedAtUtc, double SampleDurationMs,
    int IntervalSeconds, bool? OnAcPower, LiveReading[] Sensors);
public sealed record BridgeRequest(int Version, string Operation);
public sealed record BridgeReply(int Version, string Mode, bool HardwareWritesEnabled,
    string[] Operations, LiveSnapshot? Snapshot);

public static class BridgeWire
{
    public const int Version = 1;
    public const int MaximumFrameBytes = 32_768;

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, ContractJson.Options);
        if (bytes.Length > MaximumFrameBytes) throw new InvalidDataException("Oversized bridge frame.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumFrameBytes) throw new InvalidDataException("Invalid bridge frame.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return JsonSerializer.Deserialize<T>(bytes, ContractJson.Options) ?? throw new InvalidDataException("Empty bridge frame.");
    }
}
