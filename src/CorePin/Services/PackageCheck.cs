using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace CorePin.Services;

internal static class PackageCheck
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2 || !Path.IsPathFullyQualified(args[1]))
                throw new ArgumentException("Usage: CorePin.exe --package-check <absolute-result-json-path>");
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path.");
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executable)
                ?? throw new InvalidOperationException("Missing application icon.");
            var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            var topology = new CpuTopologyService().Read();
            // Inspect only this headless process, after exercising WPF's native imaging path.
            using var process = Process.GetCurrentProcess();
            string[] nativeNames = { "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "wpfgfx_cor3.dll", "PresentationNative_cor3.dll" };
            var nativeModules = process.Modules.Cast<ProcessModule>()
                .Where(module => nativeNames.Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase))
                .Select(module => new { Name = module.ModuleName, Path = module.FileName }).ToArray();
            var result = new
            {
                Product = typeof(PackageCheck).Assembly.GetName().Name,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Runtime = RuntimeInformation.FrameworkDescription,
                ExecutablePath = executable,
                OperatingSystem = RuntimeInformation.OSDescription,
                RuntimeAssemblyPath = typeof(object).Assembly.Location,
                WindowsBaseAssemblyPath = typeof(DependencyObject).Assembly.Location,
                PresentationCoreAssemblyPath = typeof(BitmapSource).Assembly.Location,
                DesktopAssemblyPath = typeof(Application).Assembly.Location,
                FormsAssemblyPath = typeof(System.Windows.Forms.NotifyIcon).Assembly.Location,
                NativeModules = nativeModules,
                LogicalProcessors = topology.Cpus.Count,
                ProcessorGroups = topology.GroupCount,
                IconWidth = image.PixelWidth,
                IconHeight = image.PixelHeight
            };
            File.WriteAllText(args[1], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
