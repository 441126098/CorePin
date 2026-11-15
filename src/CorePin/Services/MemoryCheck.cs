using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace CorePin.Services;

// Explicitly requested diagnostic runs only. No GC or working-set manipulation:
// the same sampler can compare an unchanged baseline with an optimized build.
internal static class MemoryCheck
{
    internal static async void Run(string output, Window window, Action show,
        Action programs, Action logs, Action hide, Action<int> exit)
    {
        int exitCode = 0;
        try
        {
            using var process = Process.GetCurrentProcess();
            using var writer = new StreamWriter(Path.GetFullPath(output), false, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine("# executable=" + Environment.ProcessPath);
            writer.WriteLine("# command=" + Environment.CommandLine);
            writer.WriteLine("# start=" + DateTimeOffset.Now.ToString("O"));
            writer.WriteLine("state,sample,time_shanghai,pid,working_set_bytes,private_bytes,allocated_bytes,managed_heap_bytes,handles,visible,width,height");
            var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            await Sample("tray-start", 20);
            show();
            await Sample("window", 20);
            programs();
            await Sample("programs", 10);
            logs();
            await Sample("logs", 10);
            hide();
            await Sample("tray-warm", 20);
            show();
            await Sample("reopened", 20);
            writer.WriteLine("# complete; exiting through ExitApplication");

            async Task Sample(string state, int settleSeconds)
            {
                await Task.Delay(TimeSpan.FromSeconds(settleSeconds));
                for (int sample = 1; sample <= 8; sample++)
                {
                    process.Refresh();
                    var time = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
                    writer.WriteLine(FormattableString.Invariant($"{state},{sample},{time:O},{process.Id},{process.WorkingSet64},{process.PrivateMemorySize64},{GC.GetTotalAllocatedBytes()},{GC.GetGCMemoryInfo().HeapSizeBytes},{process.HandleCount},{window.IsVisible},{window.ActualWidth},{window.ActualHeight}"));
                    if (sample < 8) await Task.Delay(1000);
                }
            }
        }
        catch (Exception ex)
        {
            // Keep diagnostics unattended even when output cannot be written.
            Console.Error.WriteLine(ex);
            exitCode = 1;
        }
        finally { exit(exitCode); }
    }
}
