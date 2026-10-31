using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ThermalPilot.Controller;

// Extracted from AsusACPI.DeviceGet/GetFan at upstream 0a1538a. Only DSTS is reachable.
internal sealed class AsusSensorReader : IDisposable
{
    private SafeFileHandle? handle;
    private static readonly uint[] Sensors = [0x00120094, 0x00120097, 0x00110013, 0x00110014, 0x00110031];
    public long ReadCalls { get; private set; }
    public int LastError { get; private set; }

    public int? Read(int sensor)
    {
        if (sensor < 0 || sensor >= Sensors.Length) throw new ArgumentOutOfRangeException(nameof(sensor));
        if (handle is null || handle.IsInvalid)
        {
            handle?.Dispose();
            handle = CreateFile(@"\\.\ATKACPI", 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            if (handle.IsInvalid) { LastError = Marshal.GetLastWin32Error(); return null; }
        }
        byte[] input = new byte[16], output = new byte[16];
        BitConverter.GetBytes(0x53545344u).CopyTo(input, 0);
        BitConverter.GetBytes(8u).CopyTo(input, 4);
        BitConverter.GetBytes(Sensors[sensor]).CopyTo(input, 8);
        ReadCalls++;
        if (!DeviceIoControl(handle, 0x0022240C, input, 16, output, 16, out var returned, IntPtr.Zero) || returned < 4)
        {
            LastError = Marshal.GetLastWin32Error();
            handle.Dispose();
            handle = null;
            return null;
        }
        var raw = BitConverter.ToUInt32(output);
        LastError = 0;
        if ((raw & 0x10000) == 0) return -1;
        var value = (int)(raw & 0xFFFF);
        return sensor < 2 ? value is > 0 and < 125 ? value : -1 : value <= 120 ? value * 100 : -1;
    }

    public void Dispose() => handle?.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint sharing, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, uint inputLength,
        byte[] output, uint outputLength, out uint returned, IntPtr overlapped);
}
