param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Drawing

# Render production XAML without opening a window, touching the mutex, or running a tuner.
# Status examples are simulated; the core map uses this PC's real CPU Set topology.
function Read-PreviewXaml([string]$Path) {
    [xml]$document = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    foreach ($attribute in @($document.SelectNodes('//@*'))) {
        if (($attribute.NamespaceURI -eq 'http://schemas.microsoft.com/winfx/2006/xaml' -and $attribute.LocalName -eq 'Class') -or
            $attribute.LocalName -in @('Click','Checked','Unchecked','Closing','SourceInitialized','TextChanged','MouseDoubleClick')) {
            [void]$attribute.OwnerElement.RemoveAttributeNode($attribute)
        }
    }
    return [System.Windows.Markup.XamlReader]::Parse($document.OuterXml)
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PreviewTopology {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool GetSystemCpuSetInformation(IntPtr data, uint length, out uint returned, IntPtr process, uint flags);
}
'@
[uint32]$length = 0
[void][PreviewTopology]::GetSystemCpuSetInformation([IntPtr]::Zero, 0, [ref]$length, [IntPtr]::Zero, 0)
$buffer = [Runtime.InteropServices.Marshal]::AllocHGlobal([int]$length)
$cpus = @()
try {
    [uint32]$returned = 0
    if (![PreviewTopology]::GetSystemCpuSetInformation($buffer, $length, [ref]$returned, [IntPtr]::Zero, 0)) { throw 'CPU Set read failed' }
    $offset = 0
    while ($offset -lt $returned) {
        $size = [Runtime.InteropServices.Marshal]::ReadInt32($buffer, $offset)
        if ($size -lt 8 -or $size -gt ($returned - $offset)) { throw 'Invalid CPU Set length' }
        if ([Runtime.InteropServices.Marshal]::ReadInt32($buffer, $offset + 4) -eq 0) {
            $cpus += [PSCustomObject]@{
                Id = [Runtime.InteropServices.Marshal]::ReadInt32($buffer, $offset + 8)
                Group = [Runtime.InteropServices.Marshal]::ReadInt16($buffer, $offset + 12)
                LogicalIndex = [Runtime.InteropServices.Marshal]::ReadByte($buffer, $offset + 14)
                CoreIndex = [Runtime.InteropServices.Marshal]::ReadByte($buffer, $offset + 15)
                EfficiencyClass = [Runtime.InteropServices.Marshal]::ReadByte($buffer, $offset + 18)
                Availability = '可用'
            }
        }
        $offset += $size
    }
} finally { [Runtime.InteropServices.Marshal]::FreeHGlobal($buffer) }
$highest = ($cpus | Measure-Object EfficiencyClass -Maximum).Maximum
$rows = @($cpus | ForEach-Object {
    $_ | Add-Member NoteProperty LogicalLabel ('G' + $_.Group + ' / CPU ' + $_.LogicalIndex)
    [PSCustomObject]@{Cpu = $_; Selected = ($_.EfficiencyClass -eq $highest); IsPerformance = ($_.EfficiencyClass -eq $highest)}
})
$performance = @($rows | Where-Object IsPerformance)
$efficiency = @($rows | Where-Object { !$_.IsPerformance })
$groups = @($performance | Group-Object { [string]$_.Cpu.Group + ':' + $_.Cpu.CoreIndex })
$application = New-Object System.Windows.Application
$application.ShutdownMode = [System.Windows.ShutdownMode]::OnExplicitShutdown
[xml]$appDocument = Get-Content -LiteralPath (Join-Path $ProjectRoot 'src\CorePin\App.xaml') -Raw -Encoding UTF8
$dictionaryXml = '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">' + $appDocument.DocumentElement.FirstChild.InnerXml + '</ResourceDictionary>'
$application.Resources = [System.Windows.Markup.XamlReader]::Parse($dictionaryXml)
$output = Join-Path $ProjectRoot 'docs\images'
New-Item -ItemType Directory -Path $output -Force | Out-Null
function Brush([string]$Color) { return [System.Windows.Media.BrushConverter]::new().ConvertFromString($Color) }

function Add-Map($Window) {
    $Window.FindName('PerformanceTitle').Text = '性能核 · ' + $performance.Count + ' 个逻辑处理器（' + $groups.Count + ' 个核心）'
    $Window.FindName('EfficiencyTitle').Text = '能效核 · ' + $efficiency.Count + ' 个逻辑处理器（默认不纳入）'
    if (!$efficiency.Count) {
        $Window.FindName('EfficiencySection').Visibility = 'Collapsed'
        $Window.FindName('TopologyNote').Visibility = 'Visible'
        $Window.FindName('TopologyNote').Text = '此处理器未报告性能等级差异，推荐使用全部逻辑处理器。'
    }
    foreach ($group in @($rows | Group-Object { [string]$_.IsPerformance + ':' + $_.Cpu.Group + ':' + $_.Cpu.CoreIndex })) {
        $p = $group.Group[0].IsPerformance
        $body = New-Object System.Windows.Controls.StackPanel
        if ($p) {
            $label = New-Object System.Windows.Controls.TextBlock
            $label.Text = '核心 ' + $group.Group[0].Cpu.CoreIndex
            $label.FontSize = 9; $label.HorizontalAlignment = 'Center'; $label.Foreground = Brush '#A77A08'
            $label.Margin = '0,0,0,2'; [void]$body.Children.Add($label)
        }
        $siblings = New-Object System.Windows.Controls.StackPanel
        $siblings.Orientation = 'Horizontal'
        foreach ($row in $group.Group) {
            $chip = Read-PreviewXaml (Join-Path $ProjectRoot 'src\CorePin\Controls\CoreChip.xaml')
            $chip.DataContext = $row
            if ($Window.FindName('NumbersBox').IsChecked) { $chip.FindName('Number').Visibility = 'Visible' }
            [void]$siblings.Children.Add($chip)
        }
        [void]$body.Children.Add($siblings)
        $surface = New-Object System.Windows.Controls.Border
        $surface.Child = $body; $surface.CornerRadius = '6'; $surface.Margin = '0,0,2,3'
        if ($p) {
            $surface.Padding = '0,1,0,3'; $surface.BorderThickness = '1'
            $surface.BorderBrush = Brush '#FFF0C2'; $surface.Background = Brush '#FFFAEB'
            [void]$Window.FindName('PerformanceMap').Children.Add($surface)
        } else { [void]$Window.FindName('EfficiencyMap').Children.Add($surface) }
    }
}
function Render-Window([string]$FileName, [int]$Width, [string]$State, [string]$OutputName) {
    $window = Read-PreviewXaml (Join-Path $ProjectRoot ('src\CorePin\' + $FileName))
    if ($FileName -eq 'MainWindow.xaml') {
        $targets = @(
            [PSCustomObject]@{Name='League of Legends.exe'; Enabled=$true; DisplayName='游戏'; Note='启用后按当前核心策略绑定'},
            [PSCustomObject]@{Name='LeagueClient.exe'; Enabled=$false; DisplayName='客户端（默认不监视）'; Note='不会绑定到性能核'},
            [PSCustomObject]@{Name='LeagueClientUx.exe'; Enabled=$false; DisplayName='客户端（默认不监视）'; Note='不会绑定到性能核'},
            [PSCustomObject]@{Name='LeagueClientUxRender.exe'; Enabled=$false; DisplayName='客户端（默认不监视）'; Note='不会绑定到性能核'})
        $model = [PSCustomObject]@{
            Status='未检测到游戏'; Summary=('推荐策略已就绪，自动绑定已开启。' + [Environment]::NewLine + '打开游戏后会自动绑定到 ' + $performance.Count + ' 个性能核。')
            Policy='当前策略：全部性能核（推荐）'; Running='● 未运行'; Targets=$targets; Processes=@()
            Logs=@('20:00:04  游戏已退出，继续等待下一次启动。','20:00:02  已将游戏绑定到性能核。','20:00:01  检测到游戏，PID 18420。','20:00:00  CorePin 已以管理员身份启动。')
        }
        if ($State -eq 'bound') {
            $model.Status = '已绑定到 ' + $performance.Count + ' 个性能核'
            $model.Summary = '只监视游戏，不监视客户端' + [Environment]::NewLine + '使用全部 ' + $performance.Count + ' 个性能核'
            $model.Running = '● 运行中'
            $window.FindName('StatusStripe').Background = Brush '#0F7A5A'
            $window.FindName('StatusCircle').Background = Brush '#E6F4ED'
            $window.FindName('StatusCircle').BorderBrush = Brush '#0F7A5A'
            $window.FindName('StatusGlyph').Text = '✓'; $window.FindName('StatusGlyph').Foreground = Brush '#0F7A5A'
            $window.FindName('RunningText').Foreground = Brush '#0F7A5A'
        }
        if ($State -eq 'admin') {
            $model.Status = '需要管理员权限'
            $model.Summary = '不提权就无法完成绑定。这不会修改游戏文件，只是允许调整处理器分配。'
            $window.FindName('ElevateButton').Visibility = 'Visible'
            $window.FindName('StatusStripe').Background = Brush '#925A0C'
            $window.FindName('StatusCircle').Background = Brush '#FFF5E3'
            $window.FindName('StatusCircle').BorderBrush = Brush '#925A0C'
            $window.FindName('StatusGlyph').Text = '!'; $window.FindName('StatusGlyph').Foreground = Brush '#925A0C'
        }
        $window.DataContext = $model
        $window.FindName('AutoPinBox').IsChecked = $true
        if ($State -eq 'selected') { $window.FindName('NumbersBox').IsChecked = $true }
        Add-Map $window
        if ($State -in @('programs','programs-compact')) { $window.FindName('ProgramsPanel').Visibility = 'Visible'; $window.FindName('ProgramsButton').Tag = 'Open' }
        if ($State -eq 'programs-compact') { $window.FindName('MainScroll').MaxHeight = 460 }
        if ($State -eq 'logs') { $window.FindName('LogsPanel').Visibility = 'Visible'; $window.FindName('LogsButton').Tag = 'Open' }
        if ($State -eq 'selected') {
            $window.FindName('CoreDetail').Visibility = 'Visible'
            $cpu = $cpus[0]
            $window.FindName('CoreDetailTitle').Text = '性能核 · ' + $cpu.LogicalLabel
            $window.FindName('CoreDetailText').Text = '物理核心：' + $cpu.CoreIndex + '（处理器组 ' + $cpu.Group + '）   逻辑编号：' + $cpu.LogicalIndex + [Environment]::NewLine +
                'EfficiencyClass：' + $cpu.EfficiencyClass + '   CPU Set ID：' + $cpu.Id + [Environment]::NewLine + '可用状态：可用   已纳入策略（保存后生效）'
        }
    }
    $content = $window.Content; $window.Content = $null
    if ($FileName -eq 'MainWindow.xaml') { $content.DataContext = $model }
    $surface = New-Object System.Windows.Controls.Border
    $previewBody = New-Object System.Windows.Controls.StackPanel
    $label = New-Object System.Windows.Controls.TextBlock
    $label.Text = 'CorePin 离屏模拟预览 · 状态与日志为示例，非实际对局验证'
    $label.Foreground = Brush '#925A0C'; $label.Background = Brush '#FFF5E3'; $label.Padding = '10,6'
    $label.FontSize = 12
    [void]$previewBody.Children.Add($label)
    [void]$previewBody.Children.Add($content)
    $surface.Background = Brush '#F3F4F7'; $surface.Child = $previewBody; $surface.Width = $Width
    [System.Windows.Documents.TextElement]::SetFontFamily($surface, $window.FontFamily)
    [System.Windows.Documents.TextElement]::SetFontSize($surface, $window.FontSize)
    if ($FileName -eq 'MainWindow.xaml') { $surface.MinHeight = 375 }
    for ($pass = 0; $pass -lt 3; $pass++) {
        $surface.Measure([System.Windows.Size]::new($Width, [double]::PositiveInfinity))
        $surface.UpdateLayout()
        [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::ApplicationIdle)
        $surface.InvalidateMeasure()
    }
    $surface.Measure([System.Windows.Size]::new($Width, [double]::PositiveInfinity))
    $height = [int][Math]::Ceiling($surface.DesiredSize.Height)
    if ($FileName -eq 'ProcessPickerWindow.xaml') { $height = 450 }
    $surface.Height = $height
    $surface.Arrange([System.Windows.Rect]::new(0,0,$Width,$height)); $surface.UpdateLayout()
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::ApplicationIdle)
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($Width,$height,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($surface)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.File]::Create((Join-Path $output $OutputName))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    $window.Close()
    Write-Output ('WPF_MARKUP_RENDER_OK: ' + $OutputName + ' ' + $Width + 'x' + $height)
}
Render-Window 'MainWindow.xaml' 920 'bound' 'main-bound.png'
Render-Window 'MainWindow.xaml' 920 'waiting' 'main-waiting.png'
Render-Window 'MainWindow.xaml' 920 'admin' 'main-admin.png'
Render-Window 'MainWindow.xaml' 920 'selected' 'core-selected.png'
Render-Window 'MainWindow.xaml' 920 'programs' 'programs.png'
Render-Window 'MainWindow.xaml' 920 'programs-compact' 'programs-compact.png'
Render-Window 'MainWindow.xaml' 920 'logs' 'logs.png'
Render-Window 'ProcessPickerWindow.xaml' 684 '' 'process-picker.png'
Render-Window 'CloseToTrayWindow.xaml' 414 '' 'close-dialog.png'
$application.Shutdown()
