# 平台和旧版本兼容

## Windows 与 .NET 官方支持

本项目发布目标是 **Windows 10/11 x64**，使用 `net10.0-windows` 和 `win-x64`，默认分发 .NET 10 LTS 多文件自包含包。不提供 ARM64 或 32 位包，不承诺所有 Windows 10/11 版本、版本类别或内部版本均获官方支持。

以下信息核对于 **2026-11-13**，后续应以微软实时文档为准：

- [.NET 支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)：.NET 8 已于 2026-11-10 结束支持；.NET 10 为 LTS，支持至 2028-11-14。维持支持还需更新到当前补丁。
- [Windows 安装与支持表](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions)和 [.NET 10 supported-OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)：支持随具体 Windows 版本及生命周期变化。当前 Windows 10 范围限于所列 LTSC / Enterprise 等版本，例如 21H2、1809、1607 对应的受支持版本类别；不是对普通消费版 Windows 10 或任意旧 build 的承诺。Windows 11 也应逐项核对，例如 26H1、25H2、24H2，以及仍在服务期的 23H2 Enterprise/Education；不能只看“Windows 11”名称。
- [Windows 10 消费版停止服务和 ESU](https://www.microsoft.com/en-us/windows/extended-security-updates)：普通支持已于 2025-10-14 结束，符合条件的 22H2 设备可按微软当时的规则加入消费者 ESU，以接收指定安全更新。ESU 的期限和资格属于 Windows 政策；它不等于一般技术支持，也不自动把该系统加入 .NET 10 官方 OS 支持表。请分别核对两项政策。

自包含表示不要求另装 .NET Desktop Runtime，不代表操作系统获延长支持。包内运行时不会被系统的 .NET 更新自动修补；需由维护者使用最新受支持补丁重新发布完整包，并由用户整体更新。包内 `CorePin.runtimeconfig.json` 的 `includedFrameworks` 记录实际捆绑版本，各运行时许可证在 `licenses` 目录。WPF 不支持裁剪，因此本项目不启用单文件、裁剪或 AOT 发布。

## 验证范围与缺口

本次验证环境为 Windows x64，系统版本 **10.0.26300.0**。Release 构建、普通及仅操作测试自身的安全检查，与 ZIP 解压后的无界面包检查分开执行；包检查用实际加载路径及主机跟踪核对随包运行时，不能当作真实 GUI 验收。

旧版进程 PID 3564 仍在运行，本次未终止、重启或提升该进程，也未更换共享锁。未完成真实窗口、托盘、UAC、SmartScreen 或实战游戏验收。没有 Windows 10 测试机，**未在 Windows 10 上测试**；也未覆盖各 Windows 11 正式版本、全部 CPU 拓扑、不同反作弊或全新无运行时机器。当前系统上的包内运行时路径证据不等于这些环境均已通过。

CorePin EXE 当前**未代码签名**。自包含不消除 UAC、SmartScreen 或进程访问权限限制；校验和用于核对文件完整性，不证明发布者身份。应阅读 [使用声明](DISCLAIMER.md)。

## 旧版本保留项

当前产品、程序集、可执行文件、命名空间及源码项目均名为 **CorePin**。以下旧名称是有意保留的兼容字符串。

| 位置 | 保留项 | 行为 |
| --- | --- | --- |
| `SettingsStore` | `%LOCALAPPDATA%\LoLCorePin\settings.json` | 仅在新文件不存在时读取、验证并原子写入 `%LOCALAPPDATA%\CorePin\settings.json`，保留旧文件。 |
| `StartupService` | HKCU Run 的 `LoLCorePin` 值名 | 启动时仅查询。只有用户明确操作“登录 Windows 后启动”时才删除旧值，并按用户选择写入 `CorePin` 或同时删除两个工具值。 |
| `App` | `Local\LoLCorePin.Desktop.v1` | 与旧版共享单实例锁，防止两个版本同时轮询并覆盖进程设置。冲突提示中的旧名称用于识别旧实例。 |
| 测试 | 旧目录、值名和示例 EXE | 仅用于隔离临时目录及内存注册表测试。 |
| GitHub 仓库 | 旧地址 `441126098/LoLCorePin` | 已更名为 [441126098/CorePin](https://github.com/441126098/CorePin)；旧地址会重定向到新仓库。 |

## 设置与启动项

新设置文件优先，即使旧文件更新也不会覆盖它。新文件损坏时不回退到旧文件，使用默认配置并暂停自动绑定，显示警告；原文件保留。旧文件损坏或迁移失败时同样暂停自动绑定并保留旧文件。迁移不更改监视范围：游戏仍为 `League of Legends.exe`，三个客户端规则仍默认关闭；用户已有自定义配置会保留。

读取旧启动项时，复选框显示已启用，但不会自动更新其 EXE 路径。升级后请明确关闭再开启一次“登录 Windows 后启动”，将路径更新到固定位置的 `CorePin.exe`。关闭操作删除两个工具值，其他应用的启动项保持不变。

启用时先删除旧启动项，再写入新启动项。旧值删除失败则不会新增新值；新值写入失败可能使登录启动关闭，界面会报告失败并重新读取状态。这两个操作不是注册表事务；此顺序避免本工具主动添加重复启动。旧版本若随后再次启用其启动项，仍可能重新创建旧值，请不要同时配置两个版本。

## 单实例与权限切换

共享锁意味着旧版仍在运行时，新版可能提示“已在运行”或因跨权限访问锁被拒绝而退出。该锁为当前 Windows 会话内的锁，并不协调其他登录会话。请从托盘正常退出旧版后再启动新版；不会自动结束任何旧进程。手动管理员重启保留原有流程：用户接受 UAC 后旧实例退出，提升后的实例等待最多 10 秒接管锁；取消 UAC 时保留原实例。

从新版降回旧版时，旧版只会读取它自己的旧设置文件；两处设置不会持续同步。请在切换前核对规则，并避免其他调优工具同时修改同一进程。
