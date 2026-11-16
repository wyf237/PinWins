# PinWins

把 Windows 桌面上的任意窗口"钉"在最前面 —— 一体积小巧、常驻托盘的窗口置顶工具。

[English](README_EN.md)

---

## 简介

PinWins 是一个基于 .NET 10 + Avalonia 的 Windows 桌面小工具。把鼠标指针移到任意窗口上，按下你自己设置的全局快捷键，该窗口就会被置顶，并绘制一圈醒目的彩色边框，方便你在多任务切换时一眼认出它。

程序常驻系统托盘，不占用任务栏，关闭设置窗口也不会退出。

## 功能特性

- **一键置顶 / 取消置顶**：鼠标指向目标窗口，按全局快捷键即可切换置顶状态，无需先激活该窗口。
- **彩色边框提示**：置顶窗口会被绘制一圈全覆盖描边；在 Windows 11 上还会同步修改窗口的原生边框颜色。
- **多窗口同时置顶**：可开启多窗口模式，多个已置顶窗口自动分配不同颜色，便于区分。
- **自定义外观**：边框颜色任意选择，宽度 1–10 px 可调（默认 6 px）。
- **自定义全局快捷键**：支持 Ctrl / Alt / Shift / Win 任意组合，可在设置界面直接按键录入。
- **已置顶窗口预览面板**：托盘菜单可打开预览面板，实时显示每个已置顶窗口的缩略图和标题：
  - 缩略图自动刷新，无需反复开关面板；
  - 点击某个卡片即可把该窗口激活到最前；
  - 当前处于最前台的窗口会以**红色边框**突出显示；
  - 卡片右上角的关闭按钮可直接取消该窗口的置顶。
- **托盘常驻**：资源管理器重启后自动恢复托盘图标；单击托盘图标打开设置，右键打开菜单。
- **单实例运行**：重复启动时不会产生多个进程，而是把已有实例的设置窗口唤到前台。
- **出错可诊断**：关键流程写入日志，异常时便于定位问题。

## 下载与版本

发布目录提供两种形式，功能完全一致，按需选择其一即可：

| 版本 | 说明 | 适用场景 |
| --- | --- | --- |
| **单文件版**（`PinWins-1.0.0-single`） | 只有一个 `PinWins.exe`，已压缩打包，首次启动会自行解压到临时目录 | 想拷贝单个文件就走，或放在 U 盘里 |
| **绿色多文件版**（`PinWins-1.0.0-portable`） | 一个文件夹，包含 exe 与全部依赖 | 启动更快，可整目录拷贝，不需要安装 |

两个版本都是 **自包含（self-contained）** 发布，**不需要**预先安装 .NET 运行时，解压即可运行。

## 快速开始

1. 下载并解压对应版本（单文件版可直接运行 exe）。
2. 首次启动会弹出设置窗口，点击"全局快捷键"区域，按下你想要的组合键（例如 `Ctrl + Alt + T`），然后点"保存"。
3. 把鼠标移到任意窗口上，按下刚设置的快捷键 —— 该窗口被置顶并出现彩色边框。
4. 再次按下快捷键（或点击预览面板上的关闭按钮），即可取消置顶。

## 设置项说明

| 设置项 | 说明 |
| --- | --- |
| 全局快捷键 | 用于切换"鼠标下窗口"的置顶状态，必须包含 Ctrl / Alt / Shift / Win 中至少一个修饰键。 |
| 边框颜色 | 置顶窗口的描边颜色，默认 `#FF3B30`（红色）。 |
| 边框宽度 | 描边宽度，范围 1–10 px，默认 6 px。数值越大越醒目，过宽会遮挡窗口边缘内容。 |
| 允许多个窗口同时置顶 | 关闭时（默认）置顶新窗口会取消上一个；开启后每个窗口自动分配不同颜色。 |

## 托盘菜单

右键单击托盘图标：

- **设置(S)…** —— 打开设置窗口
- **取消全部置顶(U)** —— 一次性移除所有窗口的置顶状态
- **已置顶窗口(P)…** —— 打开预览面板（缩略图 / 激活 / 取消置顶）
- **退出(X)** —— 退出程序并撤销全部置顶

> 单击托盘图标等同于"设置"。

## 数据与日志位置

所有文件都写在当前用户的 AppData 目录，卸载时删除该目录即可完全清理：

| 内容 | 路径 |
| --- | --- |
| 配置文件 | `%APPDATA%\PinWins\settings.json` |
| 运行日志 | `%APPDATA%\PinWins\app_yyyyMMdd.log` |
| 日志回退位置 | `%TEMP%\PinWins\app_yyyyMMdd.log` |
| 崩溃日志 | `%APPDATA%\PinWins\crash_yyyyMMdd.log` |

## 系统要求

- Windows 10 / Windows 11（64 位）
- 无需安装 .NET 运行时（发布包为 self-contained）
- 彩色原生边框需要 Windows 11；Windows 10 上仍会显示覆盖描边

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/)：

```powershell
# 调试运行
dotnet run

# 编译 Release
dotnet build -c Release

# 发布：单文件版
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o dist\PinWins-1.0.0-single

# 发布：绿色多文件版
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:DebugType=None -p:DebugSymbols=false -o dist\PinWins-1.0.0-portable
```

> 自包含发布会额外带入 SkiaSharp / HarfBuzzSharp 的 `.pdb` 调试符号（体积较大），可自行删除。

## 项目结构

```
PinWins/
├─ Program.cs                 入口：单实例互斥、崩溃日志、启动 Avalonia
├─ App.axaml(.cs)             Avalonia 应用与生命周期
├─ Core/
│  ├─ AppController.cs        主控制器：配置、热键、托盘、预览面板调度
│  ├─ PinManager.cs           置顶状态管理、颜色分配、描边维护
│  ├─ WindowHelper.cs         窗口枚举 / 命中测试 / 激活 / 置顶
│  ├─ BorderOverlay.cs        覆盖在目标窗口上的描边窗口
│  ├─ Logger.cs               轻量文件日志
│  └─ ColorUtil.cs            颜色转换
├─ Interop/                    Win32 / DWM / Shell 互操作与缩略图抓取
├─ Services/
│  ├─ GlobalHotkeyService.cs  全局热键注册
│  ├─ TrayIconService.cs      Shell_NotifyIcon 托盘图标
│  └─ SettingsService.cs      JSON 配置读写
├─ Models/                    配置与热键模型
└─ Views/                     设置窗口、已置顶窗口预览面板
```

## 已知限制

- 部分全屏游戏、UWP / 商店应用、以及使用特殊绘制的窗口无法被置顶或无法绘制描边。
- 以管理员身份运行的窗口，需要以同等权限运行本程序才能置顶。
- 若快捷键已被其它程序占用，需要在设置中更换。
- 目前仅提供 64 位（win-x64）构建。

## 许可证

[MIT](LICENSE)
