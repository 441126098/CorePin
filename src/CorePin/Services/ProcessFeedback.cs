using System;
using System.Collections.Generic;
using System.Linq;
using CorePin.Models;

namespace CorePin.Services;

public sealed record BindingFeedback(string Failure, string Banner, string[] Logs);

public static class ProcessFeedback
{
    // Win32 87 also describes a bad policy while a process is alive. A confirmed live
    // process must retain its failure; ambiguous exit details are only a fallback.
    public static bool IsExit(ProcessReport report) => report.Lifetime == ProcessLifetime.Exited ||
        (report.Lifetime == ProcessLifetime.Unknown && HasOnlyExitDetails(report.Detail));

    private static bool HasOnlyExitDetails(string detail) => detail.Length > 0 &&
        detail.Split('；').All(part => part.Contains("进程已经退出", StringComparison.Ordinal) ||
            (part.Contains("目标可能已退出或 CPU 配置已变化", StringComparison.Ordinal) &&
                (part.Contains("（Win32 6）", StringComparison.Ordinal) ||
                 part.Contains("（Win32 87）", StringComparison.Ordinal) ||
                 part.Contains("（Win32 1168）", StringComparison.Ordinal))));

    public static string FailureText(string detail, bool elevated) => detail.Contains("访问被拒绝", StringComparison.Ordinal)
        ? elevated ? "无法应用绑定。进程保护或反作弊仍可能阻止访问，请查看技术详情。" : "无法应用绑定。需要管理员权限。"
        : "无法应用绑定。请查看技术详情中的原因后重试。";

    public static BindingFeedback Evaluate(IReadOnlyList<ProcessReport> current,
        IReadOnlyDictionary<string, ProcessReport> previous, string lastFailure,
        bool elevated, bool autoPin, bool appliedManually)
    {
        var failures = current.Where(p => !p.IsCompliant && !IsExit(p)).ToArray();
        string failure = string.Join("|", failures.Select(p => p.Detail).Where(d => d.Length > 0));
        var logs = failures.Where(p => p.Detail.Length > 0 &&
                (!previous.TryGetValue(p.Identity, out var old) || old.Detail != p.Detail || IsExit(old)))
            .Select(p => FailureText(p.Detail, elevated)).Distinct().ToList();
        string banner = (autoPin && failure.Length > 0 && failure != lastFailure) ||
            (appliedManually && failures.Length > 0) ? FailureText(failure, elevated) : "";
        if (banner.Length > 0 && !logs.Contains(banner)) logs.Add(banner);
        return new BindingFeedback(failure, banner, logs.ToArray());
    }

    // Only a banner whose entire cause was exit-like may be cleared. An access
    // denial, policy failure, or unrelated error must not disappear with another PID.
    public static bool ShouldDismissBanner(IReadOnlyList<ProcessReport> shownFor, IReadOnlyList<ProcessReport> current) =>
        shownFor.Count > 0 && shownFor.All(p => HasOnlyExitDetails(p.Detail) &&
            !current.Any(n => n.Id == p.Id && !IsExit(n)));

    public static IEnumerable<string> ExitLogs(IEnumerable<ProcessReport> previous, IReadOnlyList<ProcessReport> current) =>
        previous.Where(p => !current.Any(n => !IsExit(n) && n.Identity == p.Identity))
            .Select(p => p.Name.Equals("League of Legends.exe", StringComparison.OrdinalIgnoreCase)
                ? "游戏已退出，继续等待下一次启动。" : $"{p.Name} 已退出或不再监视。");
}
