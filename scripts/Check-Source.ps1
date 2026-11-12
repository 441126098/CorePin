param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'

# Does not install software, open program files, or change any process/system setting.
$xmlFiles = Get-ChildItem -LiteralPath $ProjectRoot -File -Recurse | Where-Object { $_.Extension -in '.xaml','.csproj','.manifest' }
foreach ($file in $xmlFiles) { [xml]$null = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 }
Write-Output ('XML_OK: ' + $xmlFiles.Count + ' XAML/project/manifest files')

$native = Get-Content -LiteralPath (Join-Path $ProjectRoot 'src\LoLCorePin\Native\NativeMethods.cs') -Raw -Encoding UTF8
$probe = @'
namespace LoLCorePin.Native
{
    public static class ReadOnlyProbe
    {
        public static string Run()
        {
            if (System.IntPtr.Size != 8) throw new System.Exception("Run 64-bit PowerShell.");
            uint length;
            NativeMethods.GetSystemCpuSetInformation(System.IntPtr.Zero, 0, out length, System.IntPtr.Zero, 0);
            if (length == 0) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            System.IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)length);
            int count = 0, highest = -1, highestCount = 0;
            System.Collections.Generic.HashSet<byte> levels = new System.Collections.Generic.HashSet<byte>();
            try
            {
                uint returned;
                if (!NativeMethods.GetSystemCpuSetInformation(buffer, length, out returned, System.IntPtr.Zero, 0))
                    throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                int offset = 0;
                while (offset < returned)
                {
                    int size = System.Runtime.InteropServices.Marshal.ReadInt32(buffer, offset);
                    int type = System.Runtime.InteropServices.Marshal.ReadInt32(buffer, offset + 4);
                    if (size < 8 || size > returned - offset) throw new System.Exception("Invalid CPU Set record");
                    if (type == 0)
                    {
                        byte level = System.Runtime.InteropServices.Marshal.ReadByte(buffer, offset + 18);
                        count++; levels.Add(level);
                        if (level > highest) { highest = level; highestCount = 1; }
                        else if (level == highest) highestCount++;
                    }
                    offset += size;
                }
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
            using (SafeKernelHandle self = NativeMethods.OpenProcess(NativeMethods.QueryLimitedInformation, false,
                (uint)System.Diagnostics.Process.GetCurrentProcess().Id))
            {
                if (self.IsInvalid) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                System.UIntPtr mask, systemMask;
                if (!NativeMethods.GetProcessAffinityMask(self, out mask, out systemMask))
                    throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                uint[] ids = new uint[0]; uint required;
                bool ok = NativeMethods.GetProcessDefaultCpuSets(self, ids, 0, out required);
                if (!ok && System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 122)
                    throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                NativeMethods.PowerThrottlingState power = new NativeMethods.PowerThrottlingState(); power.Version = 1;
                bool powerOk = NativeMethods.GetProcessInformation(self, NativeMethods.ProcessPowerThrottling, ref power, 12);
                NativeMethods.ProcessEntry entry = new NativeMethods.ProcessEntry();
                entry.Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.ProcessEntry));
                int processes = 0;
                using (SafeKernelHandle snapshot = NativeMethods.CreateToolhelp32Snapshot(2, 0))
                {
                    if (snapshot.IsInvalid) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    if (!NativeMethods.Process32First(snapshot, ref entry)) throw new System.Exception("Cannot enumerate processes");
                    do { processes++; } while (NativeMethods.Process32Next(snapshot, ref entry));
                    if (System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 18) throw new System.Exception("Process enumeration incomplete");
                }
                return "NATIVE_READ_OK: logical=" + count + ", classes=" + levels.Count + ", highest=" + highest +
                    ", recommended=" + highestCount + ", groups=" + NativeMethods.GetActiveProcessorGroupCount() +
                    ", processes=" + processes + ", selfPowerReadable=" + powerOk + ", selfMask=0x" + mask.ToUInt64().ToString("X");
            }
        }
    }
}
'@
Add-Type -TypeDefinition ($native + [Environment]::NewLine + $probe)
[LoLCorePin.Native.ReadOnlyProbe]::Run()

$sources = Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'src\LoLCorePin') -Filter '*.cs' -File -Recurse
$sourceText = ($sources | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join [Environment]::NewLine
$events = @('Click','Checked','Unchecked','Closing','TextChanged','MouseDoubleClick')
foreach ($xamlFile in (Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'src\LoLCorePin') -Filter '*.xaml')) {
    [xml]$doc = Get-Content -LiteralPath $xamlFile.FullName -Raw -Encoding UTF8
    foreach ($attribute in $doc.SelectNodes('//@*')) {
        if ($attribute.LocalName -in $events -and $sourceText -notmatch ('\b' + [regex]::Escape($attribute.Value) + '\s*\(')) {
            throw ('Missing event handler: ' + $attribute.Value)
        }
    }
}
Write-Output 'XAML_HANDLERS_OK'
Write-Output 'This is a source / native read-only check; it does not replace dotnet build or the managed checks.'
