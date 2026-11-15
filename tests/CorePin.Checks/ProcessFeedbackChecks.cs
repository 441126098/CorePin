using System.ComponentModel;
using CorePin.Models;
using CorePin.Services;

internal static class ProcessFeedbackChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string game = "League of Legends.exe";
        const string exitLog = "游戏已退出，继续等待下一次启动。";
        const string retry = "无法应用绑定。请查看技术详情中的原因后重试。";
        const string admin = "无法应用绑定。需要管理员权限。";
        const string denied = "无法应用绑定。进程保护或反作弊仍可能阻止访问，请查看技术详情。";
        var bound = new ProcessReport(42, game, 100, "0", "1", "关闭", "符合当前选择", "", true, false)
            { Lifetime = ProcessLifetime.Running };
        var previous = new Dictionary<string, ProcessReport> { [bound.Identity] = bound };
        var noPrevious = new Dictionary<string, ProcessReport>();
        var exitDetails = new[] { "进程已经退出。", "读取亲和性：进程已经退出。" }
            .Concat(new[] { 6, 87, 1168 }.Select(code =>
                "读取 CPU Sets：" + ErrorText.Describe(new Win32Exception(code)))).ToArray();

        foreach (string detail in exitDetails)
        foreach (bool autoPin in new[] { false, true })
        foreach (bool manual in new[] { false, true })
        {
            var exiting = bound with { Detail = detail, IsCompliant = false, Lifetime = ProcessLifetime.Unknown };
            var feedback = ProcessFeedback.Evaluate(new[] { exiting }, previous, "", true, autoPin, manual);
            check(feedback.Banner.Length == 0 && feedback.Logs.Length == 0 && feedback.Failure.Length == 0,
                $"exit detail produces neither failure banner nor failure log (auto={autoPin}, manual={manual}): {detail}");
        }

        var gone = bound with { IsCompliant = false, Detail = "读取失败", Lifetime = ProcessLifetime.Exited };
        var goneBeforeOpen = gone with { CreationTime = 0 };
        foreach (var exiting in new[] { gone, goneBeforeOpen, bound with { Lifetime = ProcessLifetime.Exited } })
        {
            var feedback = ProcessFeedback.Evaluate(new[] { exiting }, previous, "", true, true, true);
            check(feedback.Banner.Length == 0 && feedback.Logs.Length == 0,
                "confirmed exit suppresses failures even with no exit wording or a stale success");
            var logs = ProcessFeedback.ExitLogs(previous.Values, new[] { exiting }).ToArray();
            var active = new[] { exiting }.Where(p => !ProcessFeedback.IsExit(p)).ToDictionary(p => p.Identity);
            logs = logs.Concat(ProcessFeedback.ExitLogs(active.Values, Array.Empty<ProcessReport>())).ToArray();
            check(logs.SequenceEqual(new[] { exitLog }), "exit race followed by disappearance logs normal game exit exactly once");
        }
        check(ProcessFeedback.ExitLogs(previous.Values, Array.Empty<ProcessReport>()).Single() == exitLog,
            "ordinary disappearance retains the existing normal game exit line");
        check(!ProcessFeedback.ExitLogs(noPrevious.Values, new[] { gone }).Any(),
            "first observation of an already-gone process does not invent an exit line");

        var realFailures = new[]
        {
            (Detail: "访问被拒绝（Win32 5）。", Elevated: false, Expected: admin),
            (Detail: "访问被拒绝（Win32 5）。", Elevated: true, Expected: denied),
            (Detail: "设置 CPU Sets：策略应用失败。", Elevated: true, Expected: retry)
        }.Concat(new[] { 6, 87, 1168 }.Select(code =>
            (Detail: "设置 CPU Sets：" + ErrorText.Describe(new Win32Exception(code)), Elevated: true, Expected: retry)));
        foreach (var failure in realFailures)
        foreach (bool autoPin in new[] { false, true })
        foreach (bool manual in new[] { false, true })
        {
            var running = bound with { IsCompliant = false, Detail = failure.Detail };
            var feedback = ProcessFeedback.Evaluate(new[] { running, gone }, previous, "", failure.Elevated, autoPin, manual);
            check(feedback.Banner == (autoPin || manual ? failure.Expected : "") &&
                feedback.Logs.SequenceEqual(new[] { failure.Expected }),
                $"real live failure is logged once and shown when applying (auto={autoPin}, manual={manual}): {failure.Detail}");
            check(!ProcessFeedback.ShouldDismissBanner(new[] { running }, new[] { running, gone }),
                "exit of another process cannot clear a live failure banner");
        }

        var liveFailure = bound with { IsCompliant = false, Detail = "设置 CPU Sets：策略应用失败。" };
        var first = ProcessFeedback.Evaluate(new[] { liveFailure }, previous, "", true, true, false);
        var failedPrevious = new Dictionary<string, ProcessReport> { [liveFailure.Identity] = liveFailure };
        var repeated = ProcessFeedback.Evaluate(new[] { liveFailure }, failedPrevious, first.Failure, true, true, false);
        check(first.Logs.Length == 1 && repeated.Banner.Length == 0 && repeated.Logs.Length == 0,
            "unchanged real failures do not duplicate the banner log on the next poll");
        var manualRetry = ProcessFeedback.Evaluate(new[] { liveFailure }, failedPrevious, first.Failure, true, false, true);
        check(manualRetry.Banner == retry && manualRetry.Logs.SequenceEqual(new[] { retry }),
            "manual retry still reports a persistent real failure exactly once");

        var ambiguous = bound with { IsCompliant = false, Detail = exitDetails[2] };
        check(ProcessFeedback.ShouldDismissBanner(new[] { ambiguous }, new[] { gone }) &&
            ProcessFeedback.ShouldDismissBanner(new[] { ambiguous }, Array.Empty<ProcessReport>()),
            "a previously shown exit-related banner clears on confirmed exit or disappearance");
        check(!ProcessFeedback.ShouldDismissBanner(new[] { liveFailure }, Array.Empty<ProcessReport>()) &&
            !ProcessFeedback.ShouldDismissBanner(new[] { ambiguous, liveFailure }, new[] { gone }) &&
            !ProcessFeedback.ShouldDismissBanner(Array.Empty<ProcessReport>(), new[] { gone }),
            "genuine, mixed-cause, and unrelated banners are preserved");
        foreach (string detail in new[] { "访问被拒绝（Win32 5）。", exitDetails[2] + "；设置亲和性：策略应用失败。" })
        {
            var unknown = liveFailure with { Detail = detail, Lifetime = ProcessLifetime.Unknown };
            var feedback = ProcessFeedback.Evaluate(new[] { unknown }, noPrevious, "", true, true, false);
            check(feedback.Banner.Length > 0 && feedback.Logs.Length > 0 &&
                !ProcessFeedback.ShouldDismissBanner(new[] { unknown }, new[] { gone }),
                "unknown lifetime does not suppress access denial or mixed real policy failures");
        }
    }
}
