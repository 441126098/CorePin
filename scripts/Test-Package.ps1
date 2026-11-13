param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$VerificationRoot,
    [switch]$AllowFrameworkDependent
)
$ErrorActionPreference = 'Stop'
$PackagePath = [IO.Path]::GetFullPath($PackagePath)
$VerificationRoot = [IO.Path]::GetFullPath($VerificationRoot)
if (Test-Path -LiteralPath $VerificationRoot) { throw ('VerificationRoot must be new; existing files are preserved: ' + $VerificationRoot) }
$checksumLine = (Get-Content -LiteralPath ($PackagePath + '.sha256') -Raw -Encoding UTF8).Trim()
if ($checksumLine -notmatch '^([a-fA-F0-9]{64})  (.+)$' -or $Matches[2] -cne [IO.Path]::GetFileName($PackagePath)) { throw 'Invalid ZIP checksum manifest' }
if ((Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash -ne $Matches[1]) { throw 'ZIP checksum mismatch' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
$names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
try {
    foreach ($entry in $archive.Entries) {
        $target = [IO.Path]::GetFullPath((Join-Path $VerificationRoot $entry.FullName))
        if (!$target.StartsWith($VerificationRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or !$names.Add($target)) {
            throw ('Unsafe or duplicate ZIP entry: ' + $entry.FullName)
        }
    }
} finally { $archive.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory($PackagePath, $VerificationRoot)
$manifest = Join-Path $VerificationRoot 'SHA256SUMS.txt'
$verified = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($line in (Get-Content -LiteralPath $manifest -Encoding UTF8)) {
    if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') { throw ('Invalid payload checksum line: ' + $line) }
    $expected = $Matches[1]
    $path = [IO.Path]::GetFullPath((Join-Path $VerificationRoot $Matches[2]))
    if (!$path.StartsWith($VerificationRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or !$verified.Add($path)) { throw 'Unsafe or duplicate checksum path' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw ('Payload checksum mismatch: ' + $path) }
}
$files = @(Get-ChildItem -LiteralPath $VerificationRoot -File -Recurse | Where-Object FullName -ne $manifest)
if ($files.Count -ne $verified.Count -or @($files | Where-Object { !$verified.Contains($_.FullName) }).Count -gt 0) { throw 'Unlisted payload files' }
if (@($files | Where-Object Name -like '*Checks*').Count -gt 0) { throw 'Test output in release package' }
foreach ($name in @('CorePin.exe','CorePin.dll','CorePin.deps.json','CorePin.runtimeconfig.json','INSTALL.txt','LICENSE','README.md','docs\DISCLAIMER.md','docs\COMPATIBILITY.md')) {
    if (!(Test-Path -LiteralPath (Join-Path $VerificationRoot $name) -PathType Leaf)) { throw ('Required package file missing: ' + $name) }
}
$config = Get-Content -LiteralPath (Join-Path $VerificationRoot 'CorePin.runtimeconfig.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$selfContained = [bool]$config.runtimeOptions.includedFrameworks
if (!$selfContained -and !$AllowFrameworkDependent) { throw 'Package is framework-dependent; explicit -AllowFrameworkDependent is required' }
if ($selfContained) {
    if ($config.runtimeOptions.framework -or $config.runtimeOptions.frameworks) { throw 'Ambiguous runtime configuration' }
    foreach ($framework in $config.runtimeOptions.includedFrameworks) {
        $notices = Join-Path (Join-Path $VerificationRoot 'licenses') (($framework.name + '.Runtime.win-x64').ToLowerInvariant() + '-' + $framework.version)
        if (!(Test-Path -LiteralPath $notices -PathType Container) -or !(Get-ChildItem -LiteralPath $notices -File)) { throw ('Missing bundled runtime notices: ' + $framework.name) }
    }
    foreach ($name in @('coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','PresentationFramework.dll','System.Windows.Forms.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $VerificationRoot $name) -PathType Leaf)) { throw ('Required runtime missing: ' + $name) }
    }
} elseif (!$config.runtimeOptions.framework -and !$config.runtimeOptions.frameworks) { throw 'Missing framework requirements' }
# No normal GUI launch: this explicit mode exits before profile, Run key, tray, mutex or tuning.
$reportPath = $VerificationRoot + '-result.json'
if (Test-Path -LiteralPath $reportPath) { throw ('Check result already exists: ' + $reportPath) }
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $VerificationRoot 'CorePin.exe'
$start.Arguments = '--package-check "' + $reportPath + '"'
$start.WorkingDirectory = $VerificationRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
if ($selfContained) {
    $start.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $VerificationRoot 'no-global-runtime'
    $start.EnvironmentVariables['DOTNET_ROOT_X64'] = $start.EnvironmentVariables['DOTNET_ROOT']
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
}
$process = [Diagnostics.Process]::Start($start)
try {
    if (!$process.WaitForExit(30000)) { throw ('Package check did not exit in 30 seconds; no process was terminated. PID: ' + $process.Id) }
    if ($process.ExitCode -ne 0) { throw ('Headless package check failed with exit code ' + $process.ExitCode) }
} finally { $process.Dispose() }
$result = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($result.Product -cne 'CorePin' -or $result.Architecture -cne 'X64' -or $result.LogicalProcessors -lt 1 -or $result.IconWidth -lt 1) { throw 'Invalid package check result' }
if ($selfContained) {
    foreach ($path in @($result.RuntimeAssemblyPath,$result.DesktopAssemblyPath,$result.FormsAssemblyPath)) {
        if (![IO.Path]::GetFullPath($path).StartsWith($VerificationRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw ('Runtime was loaded outside the package: ' + $path) }
    }
}
$runtimeMode = if ($selfContained) { 'package-local runtime/WPF/Forms' } else { 'framework-dependent; installed .NET Desktop runtime required' }
Write-Output ('PACKAGE_HASHES_OK: ' + $verified.Count + ' payload files and ZIP checksum')
Write-Output ('PACKAGE_HEADLESS_OK: CorePin; ' + $runtimeMode + '; logical=' + $result.LogicalProcessors + '; icon=' + $result.IconWidth + 'x' + $result.IconHeight)
Write-Output ('PACKAGE_CHECK_RESULT: ' + $reportPath)
