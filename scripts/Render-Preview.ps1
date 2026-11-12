param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase

# Load the real styles and window markup through the installed WPF runtime.
# Strip only compiled code-behind hooks. No window is shown and no tuner is run.
function Read-PreviewXaml([string]$Path) {
    [xml]$document = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    foreach ($attribute in @($document.SelectNodes('//@*'))) {
        if (($attribute.NamespaceURI -eq 'http://schemas.microsoft.com/winfx/2006/xaml' -and $attribute.LocalName -eq 'Class') -or
            $attribute.LocalName -in @('Click','Checked','Unchecked','Closing','TextChanged','MouseDoubleClick')) {
            [void]$attribute.OwnerElement.RemoveAttributeNode($attribute)
        }
    }
    return $document
}
$application = New-Object System.Windows.Application
$application.ShutdownMode = [System.Windows.ShutdownMode]::OnExplicitShutdown
[xml]$appDocument = Get-Content -LiteralPath (Join-Path $ProjectRoot 'src\LoLCorePin\App.xaml') -Raw -Encoding UTF8
$resourceContent = $appDocument.DocumentElement.FirstChild.InnerXml
$dictionaryXml = '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">' + $resourceContent + '</ResourceDictionary>'
$application.Resources = [System.Windows.Markup.XamlReader]::Parse($dictionaryXml)
$output = Join-Path $ProjectRoot 'artifacts\preview'
New-Item -ItemType Directory -Path $output -Force | Out-Null

function Render-Window([string]$FileName, [int]$Width, [int]$Height, [string]$OutputName) {
    $document = Read-PreviewXaml (Join-Path $ProjectRoot ('src\LoLCorePin\' + $FileName))
    $window = [System.Windows.Markup.XamlReader]::Parse($document.OuterXml)
    $rows = @()
    for ($index = 0; $index -lt 20; $index++) {
        $isPerformance = $index -lt 12
        $rows += [PSCustomObject]@{ Selected = $isPerformance; Kind = $(if ($isPerformance) { '性能核 P（最高等级）' } else { '较低性能等级' }); Cpu = [PSCustomObject]@{
            LogicalLabel = ('G0 / CPU ' + $index); CoreIndex = $(if ($index -lt 12) { [int][Math]::Floor($index / 2) } else { $index - 6 });
            EfficiencyClass = $(if ($isPerformance) { 1 } else { 0 }); Id = 256 + $index; Availability = '可用'
        } }
    }
    $process = [PSCustomObject]@{ Name = 'League of Legends.exe'; Id = 18420; Status = '符合当前选择';
        CpuSets = 'G0 / CPU 0, G0 / CPU 1, G0 / CPU 2, G0 / CPU 3, G0 / CPU 4, G0 / CPU 5, G0 / CPU 6, G0 / CPU 7, G0 / CPU 8, G0 / CPU 9, G0 / CPU 10, G0 / CPU 11';
        Affinity = '0x0000000000000FFF'; Qos = '执行速度节流已关闭（HighQoS）'; Detail = '' }
    $model = [PSCustomObject]@{ Status = 'LOL 正在运行 · 1 个游戏进程 · 1 / 1 个目标符合选择';
        Policy = '自动绑定开启 · 自动推荐 12 个逻辑处理器 · 500ms 轮询 · CPU Sets + 硬亲和性';
        Processes = @($process); Cpus = $rows; ManualMode = $false;
        TopologyText = '检测到 2 个性能等级 · 最高 EfficiencyClass 1 · 12 个性能核逻辑处理器（含 SMT） · 1 个处理器组';
        SelectedSummary = '已保存：12 个逻辑处理器。此处修改须保存后生效。';
        Targets = @([PSCustomObject]@{Name = 'League of Legends.exe'; Enabled = $true}, [PSCustomObject]@{Name = 'LeagueClient.exe'; Enabled = $false});
        Logs = @('20:00:00  已应用 / 修复 League of Legends.exe [18420]，读回结果：符合当前选择。');
        Notice = ''; LastCheck = '最近检查 20:00:00 · 普通用户 · 关闭窗口隐藏到托盘' }
    $window.DataContext = $model
    if ($FileName -eq 'MainWindow.xaml') {
        $window.FindName('AutoPinBox').IsChecked = $true
        $window.FindName('StatusGrid').SelectedItem = $process
    }
    $content = $window.Content
    $window.Content = $null
    $content.DataContext = $model
    $surface = New-Object System.Windows.Controls.Border
    $surface.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#F3F6F8')
    $surface.Child = $content
    $surface.Width = $Width
    $surface.Height = $Height
    $size = New-Object System.Windows.Size($Width,$Height)
    $surface.Measure($size)
    $surface.Arrange((New-Object System.Windows.Rect(0,0,$Width,$Height)))
    $surface.UpdateLayout()
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::ApplicationIdle)
    $surface.UpdateLayout()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($Width,$Height,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($surface)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.File]::Create((Join-Path $output $OutputName))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    $window.Close()
    Write-Output ('WPF_MARKUP_RENDER_OK: ' + $OutputName)
}
Render-Window 'MainWindow.xaml' 1144 820 'main.png'
Render-Window 'MainWindow.xaml' 944 740 'main-small.png'
Render-Window 'ProcessPickerWindow.xaml' 744 520 'process-picker.png'
$application.Shutdown()
