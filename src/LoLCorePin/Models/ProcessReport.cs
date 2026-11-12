namespace LoLCorePin.Models;

public sealed record ProcessReport(uint Id, string Name, long CreationTime, string CpuSets,
    string Affinity, string Qos, string Status, string Detail, bool IsCompliant, bool Changed)
{
    public string Identity => $"{Id}:{CreationTime}";
    public string Signature => $"{Identity}|{CpuSets}|{Affinity}|{Qos}|{Status}|{Detail}";
}
