using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CorePin.Native;

namespace CorePin.Services;

public sealed record ProcessItem(uint Id, string Name, uint ParentId);

public static class ProcessCatalog
{
    // Keep one native entry/name buffer per poll. The marshaller's string field in
    // ProcessEntry otherwise creates an executable-name string for every process.
    // Offsets come from the existing ABI layout used by the read-only smoke check.
    private static readonly int EntrySize = Marshal.SizeOf<NativeMethods.ProcessEntry>();
    private static readonly int NameOffset = (int)Marshal.OffsetOf<NativeMethods.ProcessEntry>(nameof(NativeMethods.ProcessEntry.ExeFile));
    private static readonly int IdOffset = (int)Marshal.OffsetOf<NativeMethods.ProcessEntry>(nameof(NativeMethods.ProcessEntry.ProcessId));
    private static readonly int ParentOffset = (int)Marshal.OffsetOf<NativeMethods.ProcessEntry>(nameof(NativeMethods.ProcessEntry.ParentProcessId));

    internal static ProcessItem[] FindTargets(HashSet<string> names, uint? onlyProcessId, out int gameProcessCount)
    {
        using var snapshot = NativeMethods.CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var lookup = names.GetAlternateLookup<ReadOnlySpan<char>>();
        var nameBuffer = new char[260];
        IntPtr entry = Marshal.AllocHGlobal(EntrySize);
        List<ProcessItem>? targets = null;
        gameProcessCount = 0;
        try
        {
            Marshal.WriteInt32(entry, EntrySize);
            if (!NativeMethods.Process32FirstBuffer(snapshot, entry))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == NativeMethods.ErrorNoMoreFiles) return Array.Empty<ProcessItem>();
                throw new Win32Exception(error);
            }
            do
            {
                uint id = unchecked((uint)Marshal.ReadInt32(entry, IdOffset));
                if (id != 0)
                {
                    Marshal.Copy(IntPtr.Add(entry, NameOffset), nameBuffer, 0, nameBuffer.Length);
                    int length = Array.IndexOf(nameBuffer, '\0');
                    var name = nameBuffer.AsSpan(0, length < 0 ? nameBuffer.Length : length);
                    // Game detection is independent of enabled rules and the test PID restriction.
                    if (name.Equals("League of Legends.exe", StringComparison.OrdinalIgnoreCase)) gameProcessCount++;
                    if ((onlyProcessId == null || id == onlyProcessId) && lookup.Contains(name))
                        (targets ??= new()).Add(new ProcessItem(id, new string(name),
                            unchecked((uint)Marshal.ReadInt32(entry, ParentOffset))));
                }
                Marshal.WriteInt32(entry, EntrySize);
            } while (NativeMethods.Process32NextBuffer(snapshot, entry));
            int lastError = Marshal.GetLastWin32Error();
            if (lastError != NativeMethods.ErrorNoMoreFiles) throw new Win32Exception(lastError);
            return targets?.ToArray() ?? Array.Empty<ProcessItem>();
        }
        finally { Marshal.FreeHGlobal(entry); }
    }

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
