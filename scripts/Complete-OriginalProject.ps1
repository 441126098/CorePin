[CmdletBinding(SupportsShouldProcess = $true)]
param([Parameter(Mandatory = $true)][string]$DestinationRoot)
$ErrorActionPreference = 'Stop'
$sourceRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$destination = [System.IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\')
if ($sourceRoot -eq $destination) { throw 'Source and destination must be different.' }
if (-not (Test-Path -LiteralPath $destination -PathType Container)) { throw 'Original project directory does not exist.' }
$baseline = Get-Content -LiteralPath (Join-Path $sourceRoot 'original-files.sha256.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$delivery = Get-Content -LiteralPath (Join-Path $sourceRoot 'delivery-files.sha256.json') -Raw -Encoding UTF8 | ConvertFrom-Json

function Get-SafePath([string]$Root, [string]$Relative) {
    $full = [System.IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $full.StartsWith($Root + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw ('Path leaves project root: ' + $Relative) }
    return $full
}
foreach ($entry in $baseline) {
    $path = Get-SafePath $destination $entry.Path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Original file is missing: ' + $entry.Path) }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Hash) { throw ('Original file changed; refusing to overwrite: ' + $entry.Path) }
}
$pending = @()
foreach ($entry in $delivery) {
    $source = Get-SafePath $sourceRoot $entry.Path
    $target = Get-SafePath $destination $entry.Path
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Hash) { throw ('Staged file changed; regenerate delivery manifest before copying: ' + $entry.Path) }
    if (Test-Path -LiteralPath $target) {
        if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.Hash) {
            throw ('Destination conflict; no files copied: ' + $entry.Path)
        }
    } else { $pending += [PSCustomObject]@{Source = $source; Target = $target} }
}
Write-Output ('Original files verified: ' + @($baseline).Count + '; missing completion files: ' + $pending.Count)
foreach ($entry in $pending) {
    if ($PSCmdlet.ShouldProcess($entry.Target, 'Copy missing completion file')) {
        [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $entry.Target))
        Copy-Item -LiteralPath $entry.Source -Destination $entry.Target
    }
}
Write-Output 'No existing file was overwritten or deleted.'
