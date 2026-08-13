using System.Runtime.InteropServices;

namespace Josha.Business
{
    // System-wide CPU and memory usage via Win32 (no PerformanceCounter — that
    // needs the System.Diagnostics.PerformanceCounter package, which the
    // project doesn't otherwise reference). CPU usage is a delta between
    // successive GetSystemTimes() samples, so it's meaningless on the very
    // first call — callers should expect 0 until the second poll.
    internal static class SystemResourceMonitorComponent
    {
        private static ulong _lastIdle;
        private static ulong _lastTotal;
        private static bool _hasPreviousSample;

        public static double GetCpuUsagePercent()
        {
            if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                return 0;

            var idle = ToUInt64(idleFt);
            var total = ToUInt64(kernelFt) + ToUInt64(userFt);

            double usage = 0;
            if (_hasPreviousSample)
            {
                var idleDelta = idle - _lastIdle;
                var totalDelta = total - _lastTotal;
                if (totalDelta > 0)
                    usage = 100.0 * (1.0 - (double)idleDelta / totalDelta);
            }

            _lastIdle = idle;
            _lastTotal = total;
            _hasPreviousSample = true;

            return Math.Clamp(usage, 0, 100);
        }

        public static double GetMemoryUsagePercent()
        {
            var status = new MEMORYSTATUSEX();
            status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            return GlobalMemoryStatusEx(ref status) ? status.dwMemoryLoad : 0;
        }

        private static ulong ToUInt64(FILETIME ft) =>
            ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }
}
