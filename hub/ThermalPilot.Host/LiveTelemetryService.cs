using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using ThermalPilot.Contracts;

namespace ThermalPilot.Host;

public sealed class LiveTelemetryService(HostSettings settings, TelemetryStore store, ILogger<LiveTelemetryService> logger) : BackgroundService
{
    private LiveSnapshot? latest;
    private string state = "not_connected";
    private string? storageError;
    private long received;
    private DateTimeOffset? lastReceived;
    private double? gapSeconds;

    public LiveSnapshot? Snapshot()
    {
        var snapshot = Volatile.Read(ref latest);
        if (snapshot is null) return null;
        var age = DateTimeOffset.UtcNow - snapshot.CapturedAtUtc;
        if (age.TotalSeconds <= Math.Max(15, snapshot.IntervalSeconds * 3)) return snapshot;
        return snapshot with
        {
            Sensors = snapshot.Sensors.Select(r => r.Value.HasValue
            ? r with { Quality = MeasurementQuality.Stale, Reason = "bridge_sample_expired" } : r).ToArray()
        };
    }

    public object Health => new
    {
        state,
        received,
        lastReceivedAtUtc = lastReceived,
        lastGapSeconds = gapSeconds,
        storageError,
        collectionEnabled = settings.CollectTelemetry,
        hardwareWritesEnabled = false
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.CollectTelemetry)
        {
            state = "collection_disabled";
            while (!stoppingToken.IsCancellationRequested)
            {
                try { store.Prune(); storageError = null; }
                catch (StorageFailure failure) { storageError = failure.Code; }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            return;
        }
        if (!OperatingSystem.IsWindows()) { state = "windows_required"; return; }
        try { latest = store.Latest(1).FirstOrDefault(); }
        catch (StorageFailure failure) { storageError = failure.Code; }
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("No owner SID.");
        var name = "ThermalPilot.Bridge.v1." + sid + "." + Process.GetCurrentProcess().SessionId;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                store.Prune();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(timeout.Token);
                await BridgeWire.WriteAsync(pipe, new BridgeRequest(1, "snapshot"), timeout.Token);
                var reply = await BridgeWire.ReadAsync<BridgeReply>(pipe, timeout.Token);
                if (reply.Version != 1 || reply.Mode != "readOnly" || reply.HardwareWritesEnabled ||
                    reply.Operations is null || !reply.Operations.SequenceEqual(new[] { "handshake", "snapshot" }))
                    throw new InvalidDataException("Bridge capability mismatch.");
                var snapshot = reply.Snapshot ?? throw new InvalidDataException("Bridge has not sampled yet.");
                Validate(snapshot);
                var previous = Volatile.Read(ref latest);
                if (previous is null || snapshot.SessionId != previous.SessionId || snapshot.Sequence > previous.Sequence)
                {
                    if (previous is not null)
                    {
                        var gap = (snapshot.CapturedAtUtc - previous.CapturedAtUtc).TotalSeconds;
                        if (snapshot.SessionId != previous.SessionId || gap > previous.IntervalSeconds * 3 + 5)
                        {
                            gapSeconds = Math.Max(0, gap);
                            store.RecordGap(snapshot.CapturedAtUtc, snapshot.SessionId != previous.SessionId ? "bridge_restarted" : "sampling_gap", gapSeconds.Value);
                        }
                    }
                    store.Append(snapshot);
                    Volatile.Write(ref latest, snapshot);
                    Interlocked.Increment(ref received);
                    lastReceived = DateTimeOffset.UtcNow;
                    storageError = null;
                }
                else if (snapshot.Sequence < previous.Sequence) throw new InvalidDataException("Bridge sequence regressed.");
                state = "connected_read_only";
                failures = 0;
            }
            catch (StorageFailure failure)
            {
                state = "storage_failed";
                if (storageError != failure.Code) logger.LogError("Telemetry storage failed: {Code}", failure.Code);
                storageError = failure.Code;
                failures++;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or OperationCanceledException or UnauthorizedAccessException)
            {
                if (stoppingToken.IsCancellationRequested) break;
                state = "bridge_unavailable";
                if (failures++ == 0) logger.LogWarning("Read-only bridge unavailable: {FailureType}", ex.GetType().Name);
            }
            var pollingSeconds = failures > 5 ? 10 : failures > 0 ? 2 : Math.Clamp(Snapshot()?.IntervalSeconds ?? 2, 2, 10);
            await Task.Delay(TimeSpan.FromSeconds(pollingSeconds), stoppingToken);
        }
    }

    private static void Validate(LiveSnapshot snapshot)
    {
        if (snapshot.IsSynthetic || snapshot.Adapter != AdapterMode.ReadOnlyBridge || snapshot.ContractVersion != 1 ||
            snapshot.SessionId == Guid.Empty || snapshot.Sequence < 1 || snapshot.IntervalSeconds is < 1 or > 30 ||
            snapshot.CapturedAtUtc > DateTimeOffset.UtcNow.AddSeconds(5) || snapshot.Sensors is null ||
            snapshot.Sensors.Length != 10 || snapshot.Sensors.Any(r => r is null) ||
            snapshot.Sensors.Select(r => r.Id).Distinct().Count() != 10)
            throw new InvalidDataException("Invalid live snapshot.");
        string[] expected = ["battery.charge", "power.ac", "battery.rate", "memory.used", "cpu.usage",
            "cpu.temperature", "gpu.temperature", "cpu.fan", "gpu.fan", "mid.fan"];
        if (!snapshot.Sensors.Select(r => r.Id).Order().SequenceEqual(expected.Order()))
            throw new InvalidDataException("Unknown live sensors.");
        foreach (var reading in snapshot.Sensors)
            if (reading.Quality is MeasurementQuality.Synthetic or MeasurementQuality.Stale ||
                reading.Value is { } value && (!double.IsFinite(value) || reading.Quality != MeasurementQuality.Fresh || reading.SampledAtUtc != snapshot.CapturedAtUtc) ||
                reading.Value is null && reading.Quality == MeasurementQuality.Fresh)
                throw new InvalidDataException("Invalid live reading.");
    }
}
