param(
    [Parameter(Mandatory = $true)][string]$PayloadPath,
    [switch]$AllowFrameworkDependent
)
$ErrorActionPreference = 'Stop'
$PayloadPath = [IO.Path]::GetFullPath($PayloadPath)
foreach ($name in @('CorePin.exe','CorePin.dll','CorePin.deps.json','CorePin.runtimeconfig.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $PayloadPath $name) -PathType Leaf)) { throw ('Incomplete payload: ' + $name) }
}
$config = Get-Content -LiteralPath (Join-Path $PayloadPath 'CorePin.runtimeconfig.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$options = $config.runtimeOptions
if ($options.tfm -cne 'net10.0') { throw ('Expected .NET 10 LTS payload; got ' + $options.tfm) }
$selfContained = @($options.includedFrameworks).Count -gt 0 -and [bool]$options.includedFrameworks
if ($selfContained -and ($options.framework -or $options.frameworks)) { throw 'Ambiguous runtime configuration' }
if (!$selfContained -and !$AllowFrameworkDependent) { throw 'Package is framework-dependent; explicit -AllowFrameworkDependent is required' }
$frameworks = if ($selfContained) { @($options.includedFrameworks) } else { @($options.framework) + @($options.frameworks) }
$frameworks = @($frameworks | Where-Object { $null -ne $_ })
foreach ($frameworkName in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) {
    $matching = @($frameworks | Where-Object name -CEQ $frameworkName)
    if ($matching.Count -ne 1 -or $matching[0].version -notmatch '^10\.0\.\d+$') { throw ('Missing or unsupported framework: ' + $frameworkName) }
}
if ($selfContained) {
    foreach ($name in @('hostfxr.dll','hostpolicy.dll','coreclr.dll','System.Private.CoreLib.dll',
        'WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll',
        'System.Windows.Forms.dll','System.Windows.Forms.Primitives.dll','System.Drawing.Common.dll',
        'wpfgfx_cor3.dll','PresentationNative_cor3.dll','D3DCompiler_47_cor3.dll','PenImc_cor3.dll','vcruntime140_cor3.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $PayloadPath $name) -PathType Leaf)) { throw ('Required bundled runtime component missing: ' + $name) }
    }
}
# Check the apphost's PE machine type before attempting to run it.
$reader = New-Object IO.BinaryReader ([IO.File]::OpenRead((Join-Path $PayloadPath 'CorePin.exe')))
try {
    if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Invalid apphost DOS header' }
    $reader.BaseStream.Position = 0x3c
    $peOffset = $reader.ReadInt32()
    $reader.BaseStream.Position = $peOffset
    if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Apphost must be Windows x64 (AMD64)' }
} finally { $reader.Dispose() }
[PSCustomObject]@{ SelfContained = $selfContained; Frameworks = $frameworks }
