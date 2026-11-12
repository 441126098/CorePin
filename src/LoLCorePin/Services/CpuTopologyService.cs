using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using LoLCorePin.Models;
using LoLCorePin.Native;

namespace LoLCorePin.Services;

public sealed class CpuTopologyService
{
    public CpuTopology Read() => ReadForProcess(IntPtr.Zero);

    internal CpuTopology ReadForProcess(IntPtr process)
    {
        // Retry when topology grows between the length query and the actual call.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            bool query = NativeMethods.GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint length, process, 0);
            int error = Marshal.GetLastWin32Error();
            if (!query && error != NativeMethods.ErrorInsufficientBuffer) throw new Win32Exception(error);
            if (length == 0 || length > 16 * 1024 * 1024) throw new InvalidOperationException("CPU Set 信息长度异常。");
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)length));
            try
            {
                if (!NativeMethods.GetSystemCpuSetInformation(buffer, length, out uint returned, process, 0))
                {
                    error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ErrorInsufficientBuffer) continue;
                    throw new Win32Exception(error);
                }
                if (returned > length) throw new InvalidOperationException("CPU Set 缓冲区长度异常。");
                var bytes = new byte[returned];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                return new CpuTopology(Parse(bytes), NativeMethods.GetActiveProcessorGroupCount());
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new InvalidOperationException("CPU 拓扑持续变化，请稍后刷新。");
    }

    // SYSTEM_CPU_SET_INFORMATION is variable-sized. Only parse documented fields;
    // never assume sizeof(struct) when advancing to the next entry.
    public static IReadOnlyList<CpuInfo> Parse(byte[] data)
    {
        var cpus = new List<CpuInfo>();
        int offset = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 8) throw new InvalidOperationException("CPU Set 数据头不完整。");
            uint size = BitConverter.ToUInt32(data, offset);
            uint type = BitConverter.ToUInt32(data, offset + 4);
            if (size < 8 || size > data.Length - offset) throw new InvalidOperationException("CPU Set 数据长度无效。");
            if (type == 0) // CpuSetInformation
            {
                if (size < 20) throw new InvalidOperationException("CPU Set 数据不完整。");
                cpus.Add(new CpuInfo(BitConverter.ToUInt32(data, offset + 8),
                    BitConverter.ToUInt16(data, offset + 12), data[offset + 14], data[offset + 15],
                    data[offset + 18], data[offset + 19]));
            }
            offset = checked(offset + (int)size);
        }
        return cpus;
    }
}
