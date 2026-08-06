#  XMG ADB

面向 Windows 的单文件 ADB 图形化运维工具。程序使用 WPF 和 .NET Framework 构建，不依赖 WebView 或 Electron。

## 功能

- USB 与 TCP/IP 设备发现、连接、断开、无线配对和批量设备操作
- 实时 Logcat、级别与 Tag 过滤、PID/包名过滤、暂停、清空和 UTF-8 导出
- APK 拖放、覆盖、测试包、降级、运行时权限、split APK 和多设备安装
- 文件推送、拉取、设备目录浏览和 SHA-256 校验
- 应用列表、启动、停止、清除数据和卸载
- 截图、录屏、设备属性、快速诊断和完整 Bugreport
- CPU、内存与负载监控
- 脚本预设、自定义 ADB 命令和多设备执行
- 可选 scrcpy 设备镜像
- 操作记录、CSV 导出、明暗主题和 ADB 环境检测

## 运行

直接双击 `dist/E300DeviceConsole.exe`。

工具会依次从以下位置寻找 `adb.exe`：

1. 设置中指定的路径
2. EXE 同目录的 `platform-tools/adb.exe`
3. EXE 同目录的 `adb.exe`
4. `ANDROID_HOME` 或 `ANDROID_SDK_ROOT`
5. 系统 `PATH`

若未检测到 ADB，程序仍可启动，并可在设置页选择本地 `adb.exe`。

## 构建

在 PowerShell 中运行：

```powershell
.\build.ps1
```

自检构建：

```powershell
.\build.ps1 -Console
.\dist\E300DeviceConsole.Tests.exe /selftest
```

## 兼容性

- Windows 7 SP1 到 Windows 11
- 需要 .NET Framework 4.8
- ADB 和 scrcpy 为外部可执行组件，可在设置中指定