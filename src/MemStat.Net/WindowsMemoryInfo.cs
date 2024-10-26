using System.Runtime.InteropServices;
namespace MemStat.Net;

public class WindowsMemoryInfo
{

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx([In] [Out] MEMORYSTATUSEX lpBuffer);

    public static void GetMemoryStatus(out double totalMemoryGB, out double availableMemoryGB)
    {
        var memoryStatus = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memoryStatus))
        {
            totalMemoryGB = ConvertBytesToGB(memoryStatus.ullTotalPhys);
            availableMemoryGB = ConvertBytesToGB(memoryStatus.ullAvailPhys);
        } else
        {
            throw new InvalidOperationException("Failed to retrieve memory status.");
        }
    }
    private static double ConvertBytesToGB(ulong bytes) => bytes / (1024.0 * 1024.0 * 1024.0);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        // @formatter:off
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        public MEMORYSTATUSEX() => dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        // @formatter:on
    }
}