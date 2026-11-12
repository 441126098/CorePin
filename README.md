# LoLCorePin · 英雄联盟性能核助手

原创 C# / .NET 8 WPF、非打包 Windows 桌面工具。通过公开 Windows API 将指定游戏进程绑定到最高 `EfficiencyClass` 的逻辑处理器，并在 Windows 11 显式关闭执行速度节流（EcoQoS）。没有驱动、注入、第三方运行库或系统级调优。

## 功能

- 默认只监视 `League of Legends.exe`；`LeagueClient.exe`、`LeagueClientUx.exe` 和 `LeagueClientUxRender.exe` 默认关闭。
- 每轮完成后等待 500ms，再枚举进程并检查设置。发现新目标或 CPU Sets、亲和性、QoS 被重置时重新应用；“立即应用”可以在暂停自动绑定时执行一次。
- 展示处理器组、组内逻辑编号、组内物理核心编号、`EfficiencyClass`、CPU Set ID、选择状态、停驻/预留状态。支持手动选择和恢复推荐。
- 展示 LOL 是否运行、每个已启用目标的 CPU Sets、亲和性掩码、QoS、验证结果及中文错误；选择进程行查看完整详情。
- 通知区域常驻；关闭主窗口隐藏到托盘，双击托盘打开，右键菜单“退出”真正退出。
- 可选当前用户登录启动；默认关闭。只修改 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 中本工具自己的 `LoLCorePin` 值。
- 设置保存在 `%LOCALAPPDATA%\LoLCorePin\settings.json`，采用临时文件后替换。最近 250 条状态变化日志只保留在内存。
- 普通用户权限启动。访问被拒绝时说明管理员启动是否可能有帮助，不自动提权。

## 环境和安装 SDK

需要 Windows 10/11 x64；Windows 11 提供完整 EcoQoS 控制。本版本固定为 x64，支持 64 位亲和性掩码；多处理器组走 CPU Sets。

构建需要 [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)（Windows x64 Installer）。只装 Runtime 无法编译，不需要 Visual Studio。安装后重新打开 PowerShell，确认：

```powershell
dotnet --info
dotnet --list-sdks
```

若新终端仍找不到 `dotnet`，确认 `C:\Program Files\dotnet\dotnet.exe` 存在，可用完整路径调用；不要把旧 .NET Framework 的 `csc.exe` 当作 SDK。

## 构建与运行

在项目根目录执行：

```powershell
dotnet build .\LoLCorePin.sln -c Release
dotnet run --project .\src\LoLCorePin\LoLCorePin.csproj -c Release --no-build
```

也可以直接运行构建结果：

```powershell
& '.\src\LoLCorePin\bin\Release\net8.0-windows\LoLCorePin.exe'
# 直接隐藏到托盘启动：
& '.\src\LoLCorePin\bin\Release\net8.0-windows\LoLCorePin.exe' --tray
```

如需发布到固定目录，并让目标机器不依赖预装 .NET 运行时：

```powershell
dotnet publish .\src\LoLCorePin\LoLCorePin.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish
& '.\artifacts\publish\LoLCorePin.exe'
```

保留完整发布目录，不要只复制 EXE。发布完成、目录固定后，再在界面中启用“登录 Windows 后启动”；移动 EXE 后需重新设置。开机启动使用 `--tray`，无需管理员权限，也不创建计划任务或服务。

## 性能核检测与应用方式

1. 调用 `GetSystemCpuSetInformation`，按每条记录的 `Size` 前进，解析 CPU Set ID、Group、LogicalProcessorIndex、CoreIndex、EfficiencyClass 和标志。
2. 只选择全机最高数值的 `EfficiencyClass`。即使存在 P / E / LP-E 三个等级，也只选最高等级。逐逻辑处理器选择，因此同时保留 P 核的两个 SMT 线程；不依据编号奇偶或 CPU 型号猜测。
3. 如果所有逻辑处理器的 `EfficiencyClass` 相同，明确显示 Windows 未报告 P/E 差异，推荐全部处理器。可以启用手动选择；不会伪造 P/E 划分。
4. 单处理器组同时设置 `SetProcessAffinityMask` 和 `SetProcessDefaultCpuSets`；亲和性掩码提供硬限制，CPU Sets 给进程未单独指定 CPU Sets 的线程提供默认选择。
5. 多处理器组使用 CPU Set ID 表达跨组选择，不把不同组混入一个 64 位掩码。CPU Sets 是软亲和性，不能保证覆盖线程独立设置或跨组硬亲和性限制，界面明确说明。
6. Windows 11 使用 `GetProcessInformation` / `SetProcessInformation` 和 `ProcessPowerThrottling`。仅将 `PROCESS_POWER_THROTTLING_EXECUTION_SPEED` 纳入 `ControlMask`，并清除其 `StateMask` 位，保留其他节流标志。Windows 10 显示不控制 EcoQoS。
7. 操作前核对句柄对应的映像文件名与创建时间，避免 PID 复用后误操作。每次设置后读回验证；只在发现不匹配时写入。
8. 应用前按目标进程查询 CPU Set 预留标志。若所选处理器专属于其他进程，报告原因并停止本轮 CPU 绑定。停驻处理器仍保留，因为停驻状态可动态变化。

手动选择用 `组号:组内逻辑编号` 持久化，不保存可能在重启后变化的 CPU Set ID。手动配置的拓扑指纹发生变化时暂停自动绑定，恢复推荐供重新确认；运行中发现拓扑变化也会停止 CPU 写入，提示刷新。

## WeGame 使用方法

1. 先运行本工具，然后照常用桌面上的 WeGame 英雄联盟快捷方式启动游戏。快捷方式名称不参与进程匹配，也不需要修改快捷方式。
2. 进入训练模式或实际对局后，默认的 `League of Legends.exe` 应出现在主窗口。只打开 WeGame 或 LOL 客户端时，没有游戏本体是正常现象。
3. 若本地区版本的游戏名称不同，在“进程与启动 → 查找正在运行的进程”中刷新，可按名称、PID 或父进程 PID 筛选。选中实际游戏 EXE，加入列表并点击“保存监视列表”。
4. 规则为完整文件名的精确、不区分大小写匹配，支持空格、中文；不接受路径或通配符。同名的多个实例都会处理。发现窗口只列出进程信息，不读取程序文件。
5. 客户端规则仅在确有需要时手动开启。不要仅因为启动器名称带有 LOL 就将整个 WeGame 当作游戏本体。

未运行游戏时无法保证本机版本的实际进程名；请以发现窗口中的真实进程为准。本工具没有读取或复制任何 Process Lasso 程序文件。

## 权限、退出和限制

- 同用户的普通进程通常可以在普通权限下设置。若目标提升运行或属于其他账户，以管理员手动启动本工具可能有帮助；重新以管理员启动前，先通过托盘退出已有实例。若已提升仍拒绝访问，界面会说明可能是进程保护或反作弊限制；本工具不绕过保护，不启用调试特权，不安装驱动。
- “符合当前选择”指进程默认 CPU Sets、单组亲和性和进程 QoS 的读回结果符合策略，不代表已逐线程采样证明实际运行核心。尤其多组系统中，线程显式 CPU Sets、线程亲和性、组限制、作业对象和系统预留可能影响实际调度。
- 轮询不能保证捕获生命周期短于 500ms 的进程，或在游戏第一条线程指令前完成设置。每轮处理耗时和系统负载也会增加响应时间。
- 暂停自动绑定、移除规则或退出只会停止后续修改；已应用的设置不会自动恢复。目标进程退出后设置自然消失。需要立即恢复时，可以在该进程仍受监视时选择全部核心并手动应用，或退出并重启游戏；选择全部核心仍会显式关闭 Windows 11 的执行速度节流。
- 如果其他调优工具同时修改相同设置，可能互相覆盖。本工具会持续修复，不承诺一定提高帧率；请根据实际体验选择是否启用。
- Windows、固件或虚拟化环境可能不报告真实混合架构；此时会明确回退全部处理器。程序不修改电源计划、优先级、注册表调度策略、游戏文件或其他系统级配置。
- 源码检查与只读 API 验证记录见 `VALIDATION.md`。

## 检查与测试

不需要 .NET SDK 的源文件 / 原生只读检查（使用系统已有的 64 位 Windows PowerShell）：

```powershell
powershell.exe -NoProfile -File .\scripts\Check-Source.ps1
```

安装 SDK 后运行无第三方测试框架的检查项目：

```powershell
dotnet run --project .\tests\LoLCorePin.Checks\LoLCorePin.Checks.csproj -c Release
# 可选：仅修改测试进程自身，验证绑定、读回、重置后修复，并在 finally 中恢复其原设置：
dotnet run --project .\tests\LoLCorePin.Checks\LoLCorePin.Checks.csproj -c Release -- --native
```

覆盖三性能等级、SMT、非混合 CPU、可变记录长度、无效数据、位 63、跨组掩码拒绝、默认客户端关闭、精确名称匹配；可选原生测试验证进程身份和 500ms 修复循环。原生测试不会修改游戏或系统进程。

`scripts\Render-Preview.ps1` 可用系统 WPF 运行时渲染主窗口及发现窗口标记。它去除编译事件挂钩并注入示例数据，只验证样式、布局和标记，不替代实际应用启动测试。输出在 `artifacts\preview`。

## 文件结构

```text
LoLCorePin.sln
src/LoLCorePin/
  App.xaml(.cs)                  应用生命周期、单实例
  MainWindow.xaml(.cs)           主窗口、托盘、策略编辑
  ProcessPickerWindow.xaml(.cs)  运行进程发现
  Models/                       CPU、规则、进程状态模型
  Native/NativeMethods.cs       Win32 声明与 SafeHandle
  Services/                     拓扑、监控、绑定、设置、开机启动
  ViewModels/MainViewModel.cs    中文界面状态
tests/LoLCorePin.Checks/          托管检查与可选自身进程测试
scripts/                        只读检查、布局预览、回填脚本
```

## 微软公开 API 文档

- [GetSystemCpuSetInformation](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemcpusetinformation) 和 [SYSTEM_CPU_SET_INFORMATION / EfficiencyClass](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_cpu_set_information)
- [CPU Sets 的软亲和性与优先规则](https://learn.microsoft.com/en-us/windows/win32/procthread/cpu-sets) 与 [处理器组](https://learn.microsoft.com/en-us/windows/win32/procthread/processor-groups)
- [SetProcessDefaultCpuSets](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessdefaultcpusets)、[SetProcessAffinityMask](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setprocessaffinitymask)
- [SetProcessInformation / HighQoS](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation)
