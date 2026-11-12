using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using LoLCorePin.Native;

namespace LoLCorePin.Services;

public sealed record ProcessItem(uint Id, string Name, uint ParentId);

public static class ProcessCatalog
{
    public static IReadOnlyList<ProcessItem> Enumerate()
    {
        using var snapshot = NativeMethods.CreateToolhelp32Snapshot(2, 0); // TH32CS_SNAPPROCESS
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var entry = new NativeMethods.ProcessEntry { Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry>() };
        var processes = new List<ProcessItem>();
        if (!NativeMethods.Process32First(snapshot, ref entry))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == NativeMethods.ErrorNoMoreFiles) return processes;
            throw new Win32Exception(error);
        }
        do
        {
            if (entry.ProcessId != 0) processes.Add(new ProcessItem(entry.ProcessId, entry.ExeFile, entry.ParentProcessId));
            entry.Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry>();
        } while (NativeMethods.Process32Next(snapshot, ref entry));
        int lastError = Marshal.GetLastWin32Error();
        if (lastError != NativeMethods.ErrorNoMoreFiles) throw new Win32Exception(lastError);
        return processes;
    }

    internal static string GetImageName(SafeKernelHandle process)
    {
        var buffer = new StringBuilder(32768);
        uint length = (uint)buffer.Capacity;
        if (!NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref length))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return Path.GetFileName(buffer.ToString());
    }
}
