using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using ThermalPilot.Contracts;

namespace ThermalPilot.Controller;

public static class Program
{
    private static LiveSnapshot? latest;
    private static readonly Guid Session = Guid.NewGuid();

    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 0) return 2;
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No owner SID.");
        var pipeName = "ThermalPilot.Bridge.v1." + user.Value + "." + Process.GetCurrentProcess().SessionId;
        try
        {
            using var singleton = new Mutex(false, @"Local\" + pipeName);
            bool owned;
            try { owned = singleton.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) { Console.Error.WriteLine("Controller already active."); return 3; }
            // A mutex is thread-affine: keep the owning thread synchronous across async work.
            try { RunAsync(pipeName, user, cancellation.Token).GetAwaiter().GetResult(); }
            finally { singleton.ReleaseMutex(); }
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Console.Error.WriteLine("Read-only controller stopped: " + ex.GetType().Name); return 1; }
    }

    private static async Task RunAsync(string name, SecurityIdentifier user, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sampling = Task.Run(() => SampleAsync(lifetime.Token), lifetime.Token);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var security = new PipeSecurity();
                security.SetAccessRuleProtection(true, false);
                security.SetOwner(user);
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                    PipeAccessRights.FullControl, AccessControlType.Deny));
                security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
                using var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096, security);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    var request = await BridgeWire.ReadAsync<BridgeRequest>(pipe, deadline.Token);
                    if (request.Version != BridgeWire.Version || request.Operation is not ("handshake" or "snapshot"))
                        continue;
                    var reply = new BridgeReply(1, "readOnly", false, ["handshake", "snapshot"], Volatile.Read(ref latest));
                    await BridgeWire.WriteAsync(pipe, reply, deadline.Token);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or OperationCanceledException)
                { Console.Error.WriteLine("Bridge request rejected: " + ex.GetType().Name); }
            }
        }
        finally
        {
            lifetime.Cancel();
            try { await sampling; } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
    }

    private static async Task SampleAsync(CancellationToken cancellationToken)
    {
        using var asus = new AsusSensorReader();
        var windows = new WindowsSensorReader();
        long sequence = 0;
        int quietSamples = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            var now = DateTimeOffset.UtcNow;
            List<LiveReading> readings = [];
            var ac = windows.Read(readings, now);
            string[] names = ["cpu.temperature", "gpu.temperature", "cpu.fan", "gpu.fan", "mid.fan"];
            for (int i = 0; i < names.Length; i++)
            {
                var value = asus.Read(i);
                var quality = value is null ? MeasurementQuality.Unavailable : value < 0 ? MeasurementQuality.Unsupported : MeasurementQuality.Fresh;
                readings.Add(new(names[i], value >= 0 ? value : null, i < 2 ? "C" : "rpm", "ASUS/ATKACPI/DSTS",
                    quality, quality == MeasurementQuality.Fresh ? now : null,
                    quality == MeasurementQuality.Fresh ? null : value is null ? "native_error_" + asus.LastError : "firmware_unsupported_or_invalid"));
            }
            var interval = ac == true ? 2 : 10;
            var cpuUse = readings.First(r => r.Id == "cpu.usage").Value;
            quietSamples = cpuUse is < 5 ? Math.Min(quietSamples + 1, 3) : 0;
            if (quietSamples == 3) interval = ac == true ? 10 : 20;
            if (readings.All(r => r.Value is null)) interval = 20;
            var snapshot = new LiveSnapshot(1, AdapterMode.ReadOnlyBridge, false, Session, ++sequence, now,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, interval, ac, readings.ToArray());
            Volatile.Write(ref latest, snapshot);
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
        }
    }
}
