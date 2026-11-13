param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$utf8 = New-Object Text.UTF8Encoding($false, $true)
$files = @(Get-ChildItem -LiteralPath $ProjectRoot -File | Where-Object { $_.Extension -in '.md','.sln' -or $_.Name -in 'LICENSE','.editorconfig','.gitignore' })
foreach ($folder in @('src','tests','scripts','docs','.github')) {
    $path = Join-Path $ProjectRoot $folder
    if (Test-Path -LiteralPath $path) {
        $files += @(Get-ChildItem -LiteralPath $path -File -Recurse | Where-Object {
            $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Extension -in '.cs','.xaml','.csproj','.manifest','.ps1','.md','.yml','.yaml'
        })
    }
}
$compatibility = @{
    'src/CorePin/App.xaml.cs' = 'shared legacy mutex and version-conflict messages'
    'src/CorePin/Services/SettingsStore.cs' = 'legacy settings directory'
    'src/CorePin/Services/StartupService.cs' = 'legacy Run value name'
    'tests/CorePin.Checks/CompatibilityChecks.cs' = 'isolated legacy fixtures'
    'README.md' = 'unchanged repository URL'
    'docs/COMPATIBILITY.md' = 'compatibility documentation'
}
$oldCount = 0
foreach ($file in $files) {
    $content = $utf8.GetString([IO.File]::ReadAllBytes($file.FullName))
    $relative = $file.FullName.Substring($ProjectRoot.TrimEnd('\').Length + 1).Replace('\','/')
    if ($content.Contains([string][char]0xfffd)) { throw ('Replacement character in ' + $relative) }
    # Construct the old name so this verifier is not itself an old-name match.
    if ($content -match ('LoL' + 'CorePin')) {
        if (!$compatibility.ContainsKey($relative)) { throw ('Unexpected legacy product name in ' + $relative) }
        $oldCount++
        Write-Output ('LEGACY_COMPAT: ' + $relative + ' - ' + $compatibility[$relative])
    }
    if ($content -match 'C:\\Users\\|D:\\workspace\\|D:\\process-lasso') { throw ('Personal absolute path in ' + $relative) }
    if ($file.Extension -eq '.ps1' -and $content -match '[^\x00-\x7f]' -and !$content.StartsWith([string][char]0xfeff)) {
        throw ('PowerShell script needs a UTF-8 BOM for Windows PowerShell: ' + $relative)
    }
}
$main = [IO.File]::ReadAllText((Join-Path $ProjectRoot 'src/CorePin/MainWindow.xaml'), $utf8)
if (!$main.Contains('Title="CorePin"') -or !$main.Contains('Text="游戏"')) { throw 'Missing product title or Chinese game label' }
foreach ($file in ($files | Where-Object Extension -eq '.xaml')) {
    if ([IO.File]::ReadAllText($file.FullName, $utf8) -match '(?i)(?:Icon|Source)\s*=\s*"[^"]*\.ico') {
        throw ('ICO must be converted from HICON in code: ' + $file.Name)
    }
}
Write-Output ('UTF8_OK: ' + $files.Count + ' source/document/config files; Chinese labels intact; no personal absolute paths')
Write-Output ('OLD_NAME_CLASSIFICATION_OK: ' + $oldCount + ' compatibility/reference files; no unexpected source leftovers')
