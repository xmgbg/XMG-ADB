<div align="center">

<img src="./assets/xmg-adb-logo-v2.png" width="180" alt="XMG_ADB Logo" />

# XMG_ADB

### 面向 Windows 的原生 ADB 图形化运维工作台

无需 WebView、Electron 或额外前端运行时，以单文件 WPF 程序完成 Android 设备管理、日志分析、应用维护、文件传输与常用诊断。

<p>
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white" alt="Windows" />
  <img src="https://img.shields.io/badge/UI-WPF-512BD4?logo=dotnet&logoColor=white" alt="WPF" />
  <img src="https://img.shields.io/badge/.NET_Framework-4.8-5C2D91" alt=".NET Framework 4.8" />
  <img src="https://img.shields.io/badge/Language-C%23-239120?logo=csharp&logoColor=white" alt="C#" />
  <img src="https://img.shields.io/badge/Build-Passing-238636" alt="Build Passing" />
</p>

[功能概览](#-功能概览) · [快速开始](#-快速开始) · [源码构建](#-源码构建) · [项目架构](#-项目架构) · [优化路线](#-优化路线)

</div>

---

## 项目简介

XMG_ADB 是一款面向 Android 开发、测试和设备运维场景的 Windows 桌面工具。它将常用 ADB 命令整合进统一的图形界面，减少重复输入命令、切换终端和手工整理日志的成本。

项目使用 **C# + WPF + .NET Framework 4.8** 构建，界面与业务逻辑均由原生代码实现。应用可在未检测到 ADB 时正常启动，并允许用户随后在设置页指定 `adb.exe` 与可选的 `scrcpy.exe`。

## ✨ 功能概览

| 模块 | 能力 |
| --- | --- |
| 设备管理 | USB / TCP/IP 设备发现、连接、无线配对、断开、刷新与批量操作 |
| 实时日志 | Logcat 实时读取、级别与 Tag 过滤、PID / 包名过滤、暂停、清空和 UTF-8 导出 |
| APK 安装 | 拖放 APK、覆盖安装、测试包、降级安装、运行时权限、Split APK 与多设备安装 |
| 文件传输 | 文件推送、拉取、设备目录浏览与 SHA-256 完整性校验 |
| 应用管理 | 应用列表、详情读取、启动、停止、清除数据与卸载 |
| 工具与诊断 | 截图、录屏、设备属性、快速诊断、Bugreport 与性能监控 |
| 自动化工具 | 脚本预设、自定义 ADB 命令和多设备执行 |
| 辅助能力 | scrcpy 镜像、操作记录、CSV 导出、明暗主题与 ADB 环境检测 |

## 🚀 快速开始

### 环境要求

- Windows 7 SP1、Windows 10 或 Windows 11
- .NET Framework 4.8
- Android Platform Tools 中的 `adb.exe`
- Android 设备已开启「开发者选项」与「USB 调试」
- `scrcpy.exe` 为可选组件，仅在需要设备镜像时使用

### 直接运行

1. 从 [GitHub Releases](https://github.com/xmgbg/XMG-ADB/releases) 下载 `XMG_ADB.exe`，或按照下文从源码构建。
2. 双击运行 `XMG_ADB.exe`。
3. 若顶部显示“未找到 ADB”，进入「设置」选择本机 `adb.exe`。
4. 连接设备并在设备端确认 USB 调试授权。

> 建议从 Android 官方渠道获取 [SDK Platform Tools](https://developer.android.com/tools/releases/platform-tools)，本项目不会自动下载或捆绑 ADB。

### ADB 搜索顺序

程序依次从以下位置查找 `adb.exe`：

1. 设置页中手动指定的路径
2. 程序目录下的 `platform-tools/adb.exe`
3. 程序目录下的 `adb.exe`
4. `ANDROID_HOME` 或 `ANDROID_SDK_ROOT`
5. 系统 `PATH`

## 🔨 源码构建

项目不依赖 NuGet 包，`build.ps1` 会调用 MSBuild 构建 `XMG_ADB.csproj`。

源码构建推荐安装 .NET Framework 4.8 Developer Pack 或 Visual Studio Build Tools；仅运行发布程序不需要开发工具。

```powershell
git clone https://github.com/xmgbg/XMG-ADB.git
cd XMG-ADB
.\build.ps1
```

GUI 构建产物：

```text
dist/XMG_ADB.exe
```

构建控制台自检版本并运行测试：

```powershell
.\build.ps1 -Console
.\dist\XMG_ADB.Tests.exe /selftest
```

预期结果：

```text
PASS 解析网络设备
PASS 解析未授权状态
PASS 保持设备选择
PASS 历史记录往返
PASS 损坏配置兼容
PASS 特殊路径配置
PASS 设置目录可写
PASS 特殊命令参数
PASS 错误输出与退出码
PASS 命令超时
PASS 命令取消
PASS 清理不完整文件
SELFTEST PASS
```

## 🧩 项目架构

```text
WPF 界面层
  MainWindow / Devices / Logcat / APK / Files / Packages / Tools / Settings
                              │
                              ▼
应用状态与任务编排
  AppState / SettingsStore / Operation History
                              │
                              ▼
ADB 进程适配层
  AdbClient / Process / Timeout / UTF-8 Output
                              │
                              ▼
adb.exe ───────────────► Android Devices
```

### 目录结构

```text
XMG-ADB/
├── src/
│   ├── Program.cs                    # 程序入口与自检
│   ├── MainWindow.cs                 # 主窗口、导航与主题
│   ├── Core.cs                       # ADB、设置、设备与操作记录核心逻辑
│   ├── PagesDevicesLog.cs            # 设备管理与 Logcat
│   ├── PagesAppsFiles.cs             # APK、文件与应用管理
│   ├── PagesToolsHistorySettings.cs  # 工具、历史和设置
│   └── Ui.cs                         # 通用 WPF 控件与样式
├── dist/                             # 本地构建产物（不提交到源码仓库）
├── app.manifest                      # Windows 清单与 DPI 配置
├── XMG_ADB.csproj                    # MSBuild 项目文件
├── build.ps1                         # 构建脚本
└── README.md
```

## ⚙️ 数据与配置

默认情况下，配置、操作记录和 Android 用户目录保存在：

```text
%LOCALAPPDATA%\XMG_ADB\
```

当该目录不可写时，程序会依次尝试程序目录下的 `data/` 和系统临时目录。应用不会将设备日志或用户配置上传到网络。

## ✅ 当前验证状态

以下项目已在 Windows 环境验证：

- GUI 与控制台版本均可成功编译
- GUI 主进程可正常启动并保持运行
- 12 项自检全部通过，覆盖设备解析与选择、历史记录、配置兼容、特殊路径、子进程错误码、超时、取消及残缺文件清理

实际设备功能仍需要连接已授权的 Android 设备，并提供可用的 `adb.exe` 进行端到端验证。

## 🗺️ 优化路线

- [x] 统一窗口、命名空间、程序集、配置目录及构建产物名称
- [x] 增加 `.gitignore`，停止跟踪本地构建产物，并通过 Release 发布 EXE
- [x] 引入 `.csproj` 与可重复的 CI 构建流程
- [x] 扩充命令超时、异常退出、设备切换和解析边界测试
- [ ] 为耗时操作增加取消令牌与更明确的进度反馈
- [ ] 对高风险操作增加二次确认、目标设备提示和命令审计
- [ ] 增加真实设备兼容性测试矩阵与界面截图

## 🤝 参与贡献

欢迎提交 Issue 或 Pull Request。提交代码前建议至少执行一次：

```powershell
.\build.ps1 -Console
.\dist\XMG_ADB.Tests.exe /selftest
```

提交信息请保持简洁，例如：

```text
feat: add package search
fix: handle offline devices
docs: update build guide
```

## 📄 开源协议

当前仓库尚未提供开源许可证。在添加明确的 `LICENSE` 前，默认版权仍归项目作者所有。

---

<div align="center">

**XMG_ADB — 让 Android 设备运维更直观。**

</div>
