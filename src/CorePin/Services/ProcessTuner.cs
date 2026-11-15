using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using CorePin.Models;
using CorePin.Native;

namespace CorePin.Services;

public sealed class ProcessTuner
{
    private sealed record State(uint[]? CpuSets, ulong? Mask, NativeMethods.PowerThrottlingState? Power,
        string CpuError, string MaskError, string PowerError);

    private static bool SupportsEcoQos => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public ProcessReport Inspect(ProcessItem item, CpuTopology topology, IReadOnlyList<CpuInfo> selected, bool apply)
    {
        long creation = 0;
        SafeKernelHandle? query = null;
        try
        {
            query = Open(item.Id, NativeMethods.QueryLimitedInformation);
            creation = ValidateIdentity(query, item.Name);
            var state = Read(query, topology.GroupCount == 1);
            var errors = new List<string>();
            bool changed = false;
            if (apply && selected.Count == 0) errors.Add("尚未选择逻辑处理器，已停止应用。至少选择一个后保存。");
            bool needsCpu = state.CpuSets == null || !state.CpuSets.Order().SequenceEqual(selected.Select(c => c.Id).Order());
            ulong desiredMask = topology.GroupCount == 1 && selected.Count > 0 ? CpuTopology.BuildMask(selected) : 0;
            bool needsMask = topology.GroupCount == 1 && state.Mask != desiredMask;
            bool needsPower = SupportsEcoQos && !PowerIsOff(state.Power);
            if (apply && selected.Count > 0 && (needsCpu || needsMask || needsPower))
            {
                try
                {
                    // These are the rights required by CPU Sets, affinity and power information.
                    // No debug privilege, injection, driver, or system-wide policy is used.
                    uint access = NativeMethods.QueryLimitedInformation;
                    if (needsCpu) access |= NativeMethods.SetLimitedInformation;
                    if (needsMask || needsPower) access |= NativeMethods.SetInformation;
                    using var writable = Open(item.Id, access);
                    if (ValidateIdentity(writable, item.Name) != creation)
                        throw new InvalidOperationException("PID 已被复用；本次未应用，将在下次轮询重新识别。");
                    bool cpuAllowed = ValidateSelection(writable, topology, selected, errors);
                    if (cpuAllowed)
                    {
                        // Hard affinity comes first: it takes precedence over CPU Sets.
                        if (needsMask) TryChange(() => NativeMethods.SetProcessAffinityMask(writable, new UIntPtr(desiredMask)), "设置亲和性", errors, ref changed);
                        if (needsCpu) TryChange(() => NativeMethods.SetProcessDefaultCpuSets(writable,
                            selected.Select(c => c.Id).ToArray(), (uint)selected.Count), "设置 CPU Sets", errors, ref changed);
                    }
                    if (needsPower)
                    {
                        // Preserve unrelated power-throttling flags (e.g. timer resolution).
                        var power = state.Power ?? new NativeMethods.PowerThrottlingState { Version = 1 };
                        bool canSetPower = state.Power.HasValue;
                        if (!canSetPower)
                        {
                            canSetPower = NativeMethods.GetProcessInformation(writable, NativeMethods.ProcessPowerThrottling, ref power, 12);
                            if (!canSetPower)
                                errors.Add("读取 QoS：" + ErrorText.Describe(new Win32Exception(Marshal.GetLastWin32Error())));
                        }
                        if (canSetPower)
                        {
                            power.ControlMask |= NativeMethods.ExecutionSpeed;
                            power.StateMask &= ~NativeMethods.ExecutionSpeed;
                            if (NativeMethods.SetProcessInformation(writable, NativeMethods.ProcessPowerThrottling, ref power, 12)) changed = true;
                            else errors.Add("关闭 EcoQoS：" + ErrorText.Describe(new Win32Exception(Marshal.GetLastWin32Error())));
                        }
                    }
                }
                catch (Exception ex) { errors.Add(ErrorText.Describe(ex)); }
                // Read back actual state; never report success just because a setter succeeded.
                state = Read(query, topology.GroupCount == 1);
            }
            if (!string.IsNullOrEmpty(state.CpuError)) errors.Add("读取 CPU Sets：" + state.CpuError);
            if (!string.IsNullOrEmpty(state.MaskError)) errors.Add("读取亲和性：" + state.MaskError);
            if (!string.IsNullOrEmpty(state.PowerError)) errors.Add("读取 QoS：" + state.PowerError);
            bool cpuOk = state.CpuSets != null && selected.Count > 0 &&
                state.CpuSets.Order().SequenceEqual(selected.Select(c => c.Id).Order());
            bool maskOk = topology.GroupCount != 1 || state.Mask == desiredMask;
            bool qosOk = !SupportsEcoQos || PowerIsOff(state.Power);
            bool compliant = cpuOk && maskOk && qosOk && errors.Count == 0;
            string status = errors.Count > 0 ? "需要处理" : compliant ? "符合当前选择" : apply ? "验证未通过" : "仅监视 / 未符合选择";
            string detail = string.Join("；", errors.Distinct());
            if (topology.GroupCount > 1)
                detail += (detail.Length > 0 ? "；" : "") + "多处理器组：使用进程默认 CPU Sets（软亲和性）；线程独立 CPU Sets、线程组和硬亲和性可能覆盖此设置。";
            return new ProcessReport(item.Id, item.Name, creation, CpuLabel(state.CpuSets, topology),
                topology.GroupCount > 1 ? "跨组：不写入 64 位掩码" : state.Mask is ulong mask ? $"0x{mask:X16}" : "无法读取",
                QosLabel(state.Power), status, detail, compliant, changed) { Lifetime = ReadLifetime(query) };
        }
        catch (Exception ex)
        {
            var lifetime = query != null ? ReadLifetime(query) : ProcessLifetime.Unknown;
            if (lifetime == ProcessLifetime.Unknown && ex is Win32Exception { NativeErrorCode: 6 or 87 or 1168 })
            {
                // A failed OpenProcess has no handle to query. Confirm a stale PID against a fresh snapshot.
                try
                {
                    if (!ProcessCatalog.Enumerate().Any(p => p.Id == item.Id)) lifetime = ProcessLifetime.Exited;
                }
                catch { /* Keep an unconfirmed failure visible in the technical details. */ }
            }
            return new ProcessReport(item.Id, item.Name, creation, "无法读取", "无法读取", "无法读取",
                "无法访问 / 已退出", ErrorText.Describe(ex), false, false) { Lifetime = lifetime };
        }
        finally { query?.Dispose(); }
    }

    private static ProcessLifetime ReadLifetime(SafeKernelHandle process) =>
        !NativeMethods.GetExitCodeProcess(process, out uint code) ? ProcessLifetime.Unknown :
        code == 259 ? ProcessLifetime.Running : ProcessLifetime.Exited;

    private static SafeKernelHandle Open(uint pid, uint access)
    {
        var handle = NativeMethods.OpenProcess(access, false, pid);
        if (!handle.IsInvalid) return handle;
        int error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new Win32Exception(error);
    }

    private static long ValidateIdentity(SafeKernelHandle process, string expectedName)
    {
        if (!string.Equals(ProcessCatalog.GetImageName(process), expectedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("进程名称已变化（可能是 PID 复用），本次不操作。");
        if (!NativeMethods.GetProcessTimes(process, out long creation, out _, out _, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!NativeMethods.GetExitCodeProcess(process, out uint exitCode)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (exitCode != 259) throw new InvalidOperationException("进程已经退出。");
        return creation;
    }

    private static bool ValidateSelection(SafeKernelHandle process, CpuTopology expected, IReadOnlyList<CpuInfo> selected, List<string> errors)
    {
        var current = new CpuTopologyService().ReadForProcess(process.DangerousGetHandle());
        if (current.Fingerprint != expected.Fingerprint || selected.Any(c => !current.Cpus.Any(n => n.Id == c.Id && n.Key == c.Key)))
        {
            errors.Add("CPU 拓扑已经变化；为避免绑定错误，已停止应用。请点击“刷新拓扑”并重新保存选择。");
            return false;
        }
        var reserved = current.Cpus.Where(c => selected.Any(s => s.Id == c.Id) && c.IsAllocated && (c.Flags & 4) == 0).ToArray();
        if (reserved.Length == 0) return true;
        errors.Add("所选 CPU Set 被系统保留给其他进程：" + string.Join(", ", reserved.Select(c => c.LogicalLabel)) + "。请手动取消这些处理器。");
        return false;
    }

    private static void TryChange(Func<bool> action, string operation, List<string> errors, ref bool changed)
    {
        try
        {
            if (action()) changed = true;
            else errors.Add(operation + "：" + ErrorText.Describe(new Win32Exception(Marshal.GetLastWin32Error())));
        }
        catch (Exception ex) { errors.Add(operation + "：" + ErrorText.Describe(ex)); }
    }

    private static State Read(SafeKernelHandle process, bool readMask)
    {
        uint[]? ids = null;
        ulong? mask = null;
        NativeMethods.PowerThrottlingState? power = null;
        string cpuError = "", maskError = "", powerError = "";
        try { ids = ReadCpuSets(process); } catch (Exception ex) { cpuError = ErrorText.Describe(ex); }
        if (readMask)
        {
            if (NativeMethods.GetProcessAffinityMask(process, out UIntPtr value, out _)) mask = value.ToUInt64();
            else maskError = ErrorText.Describe(new Win32Exception(Marshal.GetLastWin32Error()));
        }
        if (SupportsEcoQos)
        {
            try
            {
                var value = new NativeMethods.PowerThrottlingState { Version = 1 };
                if (NativeMethods.GetProcessInformation(process, NativeMethods.ProcessPowerThrottling, ref value, 12)) power = value;
                else powerError = ErrorText.Describe(new Win32Exception(Marshal.GetLastWin32Error()));
            }
            catch (Exception ex) { powerError = ErrorText.Describe(ex); }
        }
        return new State(ids, mask, power, cpuError, maskError, powerError);
    }

    internal static uint[] ReadCpuSets(SafeKernelHandle process)
    {
        uint[] ids = Array.Empty<uint>();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (NativeMethods.GetProcessDefaultCpuSets(process, ids, (uint)ids.Length, out uint count))
                return ids.Take(checked((int)count)).ToArray();
            int error = Marshal.GetLastWin32Error();
            if (error != NativeMethods.ErrorInsufficientBuffer) throw new Win32Exception(error);
            if (count > 65536) throw new InvalidOperationException("CPU Set 数量异常。");
            ids = new uint[count];
        }
        throw new InvalidOperationException("进程 CPU Sets 持续变化，暂时无法读取。");
    }

    private static bool PowerIsOff(NativeMethods.PowerThrottlingState? state) => state is { } p &&
        (p.ControlMask & NativeMethods.ExecutionSpeed) != 0 && (p.StateMask & NativeMethods.ExecutionSpeed) == 0;

    private static string QosLabel(NativeMethods.PowerThrottlingState? state) => !SupportsEcoQos ? "Windows 10：不控制 EcoQoS" :
        state is not { } p ? "无法读取" : PowerIsOff(p) ? "执行速度节流已关闭（HighQoS）" :
        (p.ControlMask & p.StateMask & NativeMethods.ExecutionSpeed) != 0 ? "EcoQoS 已启用" : "系统管理（未显式关闭）";

    private static string CpuLabel(uint[]? ids, CpuTopology topology) => ids == null ? "无法读取" : ids.Length == 0 ? "系统默认（未指定）" :
        string.Join(", ", ids.Select(id => topology.Cpus.FirstOrDefault(c => c.Id == id)?.LogicalLabel ?? $"CPU Set #{id}"));
}
