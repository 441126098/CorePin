namespace CorePin.Models;

public enum ProcessLifetime { Unknown, Running, Exited }

public sealed record ProcessReport(uint Id, string Name, long CreationTime, string CpuSets,
    string Affinity, string Qos, string Status, string Detail, bool IsCompliant, bool Changed)
{
    public ProcessLifetime Lifetime { get; init; }
    public string PowerSummary => "处理器省电限制：" + (Qos.Contains("无法读取") ? "无法读取" : Qos.Contains("已关闭") ? "已关闭" : "系统管理");
    public string Identity => $"{Id}:{CreationTime}";
    public string Signature => $"{Identity}|{CpuSets}|{Affinity}|{Qos}|{Status}|{Detail}|{Lifetime}";
}
