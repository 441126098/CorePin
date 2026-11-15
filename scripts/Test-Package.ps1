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
$payloadInfo = & (Join-Path $PSScriptRoot 'Test-PublishPayload.ps1') -PayloadPath $VerificationRoot -AllowFrameworkDependent:$AllowFrameworkDependent
$selfContained = $payloadInfo.SelfContained
if ($selfContained) {
    foreach ($framework in $payloadInfo.Frameworks) {
        $notices = Join-Path (Join-Path $VerificationRoot 'licenses') (($framework.name + '.Runtime.win-x64').ToLowerInvariant() + '-' + $framework.version)
        if (!(Test-Path -LiteralPath $notices -PathType Container) -or !(Get-ChildItem -LiteralPath $notices -File)) { throw ('Missing bundled runtime notices: ' + $framework.name) }
    }
}
# No normal GUI launch: this explicit mode exits before profile, Run key, tray, mutex or tuning.
$reportPath = $VerificationRoot + '-result.json'
if (Test-Path -LiteralPath $reportPath) { throw ('Check result already exists: ' + $reportPath) }
$tracePath = $VerificationRoot + '-host-trace.txt'
if (Test-Path -LiteralPath $tracePath) { throw ('Host trace already exists: ' + $tracePath) }
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $VerificationRoot 'CorePin.exe'
$start.Arguments = '--package-check "' + $reportPath + '"'
$start.WorkingDirectory = $VerificationRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.EnvironmentVariables['DOTNET_HOST_TRACE'] = '1'
$start.EnvironmentVariables['DOTNET_HOST_TRACEFILE'] = $tracePath
$start.EnvironmentVariables['DOTNET_HOST_TRACE_VERBOSITY'] = '4'
if ($selfContained) {
    # A deliberately invalid root is only a guard. Loaded module paths below are the proof.
    $start.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $VerificationRoot 'no-global-runtime'
    $start.EnvironmentVariables['DOTNET_ROOT_X64'] = $start.EnvironmentVariables['DOTNET_ROOT']
}
$process = [Diagnostics.Process]::Start($start)
try {
    if (!$process.WaitForExit(30000)) { throw ('Package check did not exit in 30 seconds; no process was terminated. PID: ' + $process.Id) }
    if ($process.ExitCode -ne 0) { throw ('Headless package check failed with exit code ' + $process.ExitCode) }
} finally { $process.Dispose() }
$result = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($result.Product -cne 'CorePin' -or $result.Architecture -cne 'X64' -or $result.LogicalProcessors -lt 1 -or $result.IconWidth -lt 1) { throw 'Invalid package check result' }
if ($result.ExecutablePath -ine (Join-Path $VerificationRoot 'CorePin.exe')) { throw 'Unexpected executable path' }
if (!(Test-Path -LiteralPath $tracePath -PathType Leaf) -or (Get-Item -LiteralPath $tracePath).Length -eq 0) { throw 'Missing documented host trace' }
if ($selfContained) {
    foreach ($path in @($result.RuntimeAssemblyPath,$result.WindowsBaseAssemblyPath,$result.PresentationCoreAssemblyPath,$result.DesktopAssemblyPath,$result.FormsAssemblyPath)) {
        if (!$path) { throw 'Missing loaded managed assembly evidence' }
        if (![IO.Path]::GetFullPath($path).StartsWith($VerificationRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw ('Runtime was loaded outside the package: ' + $path) }
    }
    foreach ($name in @('hostfxr.dll','hostpolicy.dll','coreclr.dll','wpfgfx_cor3.dll','PresentationNative_cor3.dll')) {
        $modules = @($result.NativeModules | Where-Object Name -IEQ $name)
        if ($modules.Count -ne 1 -or $modules[0].Path -ine (Join-Path $VerificationRoot $name)) { throw ('Missing package-local loaded native module: ' + $name) }
    }
    $bundledRuntime = @($payloadInfo.Frameworks | Where-Object name -CEQ 'Microsoft.NETCore.App')[0].version
    if ($result.Runtime -cne ('.NET ' + $bundledRuntime)) { throw ('Unexpected executing runtime: ' + $result.Runtime) }
    Write-Output ('PACKAGE_RUNTIME_PROOF_OK: loaded hostfxr/hostpolicy/coreclr/WPF native modules and managed runtime/WPF/Forms from ' + $VerificationRoot)
}
$runtimeMode = if ($selfContained) { 'package-local runtime/WPF/Forms' } else { 'framework-dependent; installed .NET Desktop runtime required' }
Write-Output ('PACKAGE_HASHES_OK: ' + $verified.Count + ' payload files and ZIP checksum')
Write-Output ('PACKAGE_HEADLESS_OK: CorePin; ' + $runtimeMode + '; logical=' + $result.LogicalProcessors + '; icon=' + $result.IconWidth + 'x' + $result.IconHeight)
Write-Output ('PACKAGE_CHECK_RESULT: ' + $reportPath)
Write-Output ('PACKAGE_HOST_TRACE: ' + $tracePath)
