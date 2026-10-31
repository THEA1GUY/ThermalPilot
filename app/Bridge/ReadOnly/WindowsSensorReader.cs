using System.Diagnostics;
using System.Runtime.InteropServices;
using ThermalPilot.Contracts;

namespace ThermalPilot.Controller;

// Native read queries extracted from HardwareControl; no legacy static state or provider initialization.
internal sealed class WindowsSensorReader
{
    private ulong idleBefore, kernelBefore, userBefore;
    private long previousTick;

    public bool? Read(List<LiveReading> readings, DateTimeOffset now)
    {
        var power = new PowerStatus();
        bool powerOk = GetSystemPowerStatus(ref power);
        Add("battery.charge", powerOk && power.Percent <= 100 ? power.Percent : null, "%", "GetSystemPowerStatus");
        bool? ac = powerOk && power.Ac <= 1 ? power.Ac == 1 : null;
        Add("power.ac", ac.HasValue ? ac.Value ? 1 : 0 : null, "boolean", "GetSystemPowerStatus");
        uint result = CallNtPowerInformation(5, IntPtr.Zero, 0, out var battery, (uint)Marshal.SizeOf<BatteryState>());
        bool batteryOk = result == 0 && battery.Present != 0;
        Add("battery.rate", batteryOk && battery.Rate != int.MinValue ? battery.Rate / 1000.0 : null,
            "W", "CallNtPowerInformation/SystemBatteryState");
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        Add("memory.used", GlobalMemoryStatusEx(ref memory) ? memory.Load : null, "%", "GlobalMemoryStatusEx");
        double? usage = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var tick = Stopwatch.GetTimestamp();
            if (previousTick != 0 && Stopwatch.GetElapsedTime(previousTick, tick).TotalSeconds < 30 &&
                idle >= idleBefore && kernel >= kernelBefore && user >= userBefore)
            {
                var total = kernel - kernelBefore + user - userBefore;
                var idleDelta = idle - idleBefore;
                if (total > 0 && idleDelta <= total) usage = 100.0 * (total - idleDelta) / total;
            }
            (idleBefore, kernelBefore, userBefore, previousTick) = (idle, kernel, user, tick);
        }
        Add("cpu.usage", usage, "%", "GetSystemTimes");
        return ac;

        void Add(string id, double? value, string unit, string source) =>
            readings.Add(new(id, value, unit, source, value.HasValue ? MeasurementQuality.Fresh : MeasurementQuality.Unavailable,
                value.HasValue ? now : null, value.HasValue ? null : "native_query_unavailable_or_initial_baseline"));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryState
    {
        public byte Ac, Present, Charging, Discharging, Spare1, Spare2, Spare3, Tag;
        public uint MaxCapacity, RemainingCapacity;
        public int Rate;
        public uint EstimatedTime, DefaultAlert1, DefaultAlert2;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte Ac, Flag, Percent, SystemStatus;
        public int Lifetime, FullLifetime;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputLength, out BatteryState output, uint outputLength);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(ref PowerStatus status);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
