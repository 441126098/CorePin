using System;
using System.IO;
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
            var topology = new CpuTopologyService().Read();
            var result = new
            {
                Product = typeof(PackageCheck).Assembly.GetName().Name,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeAssemblyPath = typeof(object).Assembly.Location,
                DesktopAssemblyPath = typeof(Application).Assembly.Location,
                FormsAssemblyPath = typeof(System.Windows.Forms.NotifyIcon).Assembly.Location,
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
