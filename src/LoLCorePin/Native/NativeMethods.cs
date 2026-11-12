using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LoLCorePin.Native
{
    // Public Win32 APIs only. Layouts also remain compilable by Windows PowerShell's
    // framework compiler for the read-only smoke check on machines without an SDK.
    internal sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeKernelHandle() : base(true) { }
        protected override bool ReleaseHandle() { return NativeMethods.CloseHandle(handle); }
    }

    internal static class NativeMethods
    {
        internal const uint QueryLimitedInformation = 0x1000;
        internal const uint SetLimitedInformation = 0x2000;
        internal const uint SetInformation = 0x0200;
        internal const uint QueryInformation = 0x0400;
        internal const int ErrorAccessDenied = 5;
        internal const int ErrorInsufficientBuffer = 122;
        internal const int ErrorNoMoreFiles = 18;
        internal const int ProcessPowerThrottling = 4;
        internal const uint ExecutionSpeed = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct PowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ProcessEntry
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public UIntPtr DefaultHeapId;
            public uint ModuleId;
            public uint Threads;
            public uint ParentProcessId;
            public int PriorityClassBase;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeKernelHandle OpenProcess(uint access,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemCpuSetInformation(IntPtr information, uint bufferLength,
            out uint returnedLength, IntPtr process, uint flags);

        [DllImport("kernel32.dll")]
        internal static extern ushort GetActiveProcessorGroupCount();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProcessDefaultCpuSets(SafeKernelHandle process,
            uint[] cpuSetIds, uint count);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessDefaultCpuSets(SafeKernelHandle process,
            [Out] uint[] cpuSetIds, uint count, out uint requiredCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessAffinityMask(SafeKernelHandle process,
            out UIntPtr processMask, out UIntPtr systemMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProcessAffinityMask(SafeKernelHandle process, UIntPtr mask);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessInformation(SafeKernelHandle process, int informationClass,
            ref PowerThrottlingState information, uint informationSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProcessInformation(SafeKernelHandle process, int informationClass,
            ref PowerThrottlingState information, uint informationSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(SafeKernelHandle process, uint flags,
            StringBuilder fileName, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(SafeKernelHandle process, out long creation,
            out long exit, out long kernel, out long user);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeProcess(SafeKernelHandle process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeKernelHandle CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32First(SafeKernelHandle snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32Next(SafeKernelHandle snapshot, ref ProcessEntry entry);
    }
}
