param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\corepin-public-prep\release'),
    [switch]$FrameworkDependent,
    [switch]$NoRestore,
    [string]$BuildArtifactsPath
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw ('OutputRoot must be new; existing output is preserved: ' + $OutputRoot) }
[void][IO.Directory]::CreateDirectory($OutputRoot)
$payload = Join-Path $OutputRoot 'publish'
$build = if ($BuildArtifactsPath) { [IO.Path]::GetFullPath($BuildArtifactsPath) } else { Join-Path $OutputRoot 'build' }
$project = Join-Path $ProjectRoot 'src\CorePin\CorePin.csproj'
$publishArguments = @('publish',$project,'-c','Release','--artifacts-path',$build,'-o',$payload,'-p:PublishSingleFile=false','-p:PublishTrimmed=false','-p:DebugType=None','-p:DebugSymbols=false')
if ($FrameworkDependent) { $publishArguments += @('--self-contained','false') }
else { $publishArguments += @('-r','win-x64','--self-contained','true') }
if ($NoRestore) { $publishArguments += '--no-restore' }
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw ('dotnet publish failed with exit code ' + $LASTEXITCODE + '; partial output preserved at ' + $OutputRoot) }
$required = @('CorePin.exe','CorePin.dll','CorePin.deps.json','CorePin.runtimeconfig.json')
if (!$FrameworkDependent) { $required += @('coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','PresentationFramework.dll','System.Windows.Forms.dll') }
foreach ($name in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $payload $name) -PathType Leaf)) { throw ('Incomplete payload: ' + $name) }
}
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'LICENSE'),(Join-Path $ProjectRoot 'README.md') -Destination $payload
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'docs') -Destination (Join-Path $payload 'docs') -Recurse
if (!$FrameworkDependent) {
    # Retain the restored runtime packs' own notices separately from the project's MIT license.
    $assets = Get-Content -LiteralPath (Join-Path $build 'obj\CorePin\project.assets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $runtime = Get-Content -LiteralPath (Join-Path $payload 'CorePin.runtimeconfig.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
        $pack = ($framework.name + '.Runtime.win-x64').ToLowerInvariant()
        $packRoot = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path (Join-Path $folder $pack) $framework.version
            if (Test-Path -LiteralPath $candidate -PathType Container) { $packRoot = $candidate; break }
        }
        if (!$packRoot) { throw ('Cannot locate restored runtime notices: ' + $pack) }
        $notices = @(Get-ChildItem -LiteralPath $packRoot -File | Where-Object { $_.Name -match '(?i)license|notice' -and $_.Extension -in '.txt','.md' })
        if (!$notices.Count) { throw ('Missing runtime license/notice files: ' + $packRoot) }
        $destination = Join-Path (Join-Path $payload 'licenses') ($pack + '-' + $framework.version)
        [void][IO.Directory]::CreateDirectory($destination)
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $destination }
    }
}
$utf8 = New-Object Text.UTF8Encoding($false)
$install = if ($FrameworkDependent) {
    "CorePin win-x64 (framework-dependent)`nRequires the .NET 8 Windows Desktop Runtime x64 installed separately.`nExtract ALL files, then run CorePin.exe. See README.md and docs/DISCLAIMER.md.`n"
} else {
    "CorePin win-x64 (self-contained)`nIncludes .NET / Windows Desktop runtime components and their license notices.`nExtract ALL files, then run CorePin.exe. See README.md and docs/DISCLAIMER.md.`n"
}
[IO.File]::WriteAllText((Join-Path $payload 'INSTALL.txt'), $install, $utf8)
$lines = @(Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($payload.Length + 1).Replace('\','/')
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $relative
})
[IO.File]::WriteAllLines((Join-Path $payload 'SHA256SUMS.txt'), $lines, $utf8)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageName = if ($FrameworkDependent) { 'CorePin-win-x64-framework-dependent.zip' } else { 'CorePin-win-x64.zip' }
$zip = Join-Path $OutputRoot $packageName
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($zip) + "`n"), $utf8)
Write-Output ('PACKAGE_CREATED: ' + $zip)
Write-Output ('PAYLOAD_FILES_HASHED: ' + $lines.Count)
Write-Output ('ZIP_SHA256: ' + $hash)
