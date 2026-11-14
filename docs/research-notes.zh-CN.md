# 调研依据与验证边界

调研日期为 2026 年 11 月 13 日。以用户确认的 Windows 原生命令行和 Android 14 平板为首版目标。

## 1. 用户已经确认的条件

| 项目 | 已确认内容 |
| --- | --- |
| 平板 | 联想小新 Pad Pro 12.7 |
| 系统 | Android 14，ZUI 16.0.474 稳定版 |
| PC 产品形态 | 独立托盘小工具，可以通过工具或专用命令启动受管理的终端 |
| 日常终端 | Windows 原生 PowerShell / Windows Terminal |
| 提醒范围 | 首版 App 保持前台或常亮，完全本地，无需云端 |
| USB | 用户接受并已开启 USB 调试，已连接数据线 |
| 无线调试 | 用户提供当前连接地址 `10.152.56.63:43585` |
| 蓝牙名称 | `Zaradee : Clear all Brilliancy & Remember The Brilliancy` |

已从 USB 真机读取具体产品型号 `TB371FC`，Android 版本与系统构建均符合用户描述。蓝牙及电池策略以这台真机的后续验证结果为准。

## 2. 连接方式的原始依据

### 2.1 USB ADB

Android 官方提供 USB 调试及电脑 RSA 授权流程。ADB 手册提供 `reverse REMOTE LOCAL`，可把设备上的端口转发至电脑上的监听端口。这允许 Android App 通过本机地址访问 PC 的本地服务，物理传输经过 USB。

依据为 [Android Debug Bridge](https://developer.android.com/tools/adb) 和 [ADB 命令手册](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/docs/user/adb.1.md)。

本次已从 Google 官方地址下载 Windows Platform Tools，实际版本为 `37.0.1-15733141`。工具放在项目 `.tools/platform-tools/`，未安装系统驱动。下载来源及版本发布说明见 [SDK Platform Tools](https://developer.android.com/tools/releases/platform-tools)。

用户已在设备允许调试。本机以 `adb -d` 确认 USB 传输，成功创建临时 reverse 映射，由平板发送 1024 字节数据至 PC 并收到内容匹配的确认消息，随后移除该映射。设备型号为 TB371FC，Android 14，构建版本包含 ZUI 16.0.474。原始证据见 [usb-link-evidence.json](../research/usb-link-evidence.json)。测试没有安装 APK。

### 2.2 蓝牙 RFCOMM

Android 和 Windows 均公开 RFCOMM 数据流接口。Android 提供 `BluetoothSocket`，Windows 提供 `Windows.Devices.Bluetooth.Rfcomm` 与 `StreamSocket`。双方使用相同的自定义服务 UUID，建立双向字节流，不需要 PAN、热点或互联网。

依据为 [Android 连接蓝牙设备](https://developer.android.com/develop/connectivity/bluetooth/connect-bluetooth-devices)、[Windows RFCOMM](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/send-or-receive-files-with-rfcomm) 和 [微软 RFCOMM 聊天示例](https://learn.microsoft.com/en-us/samples/microsoft/windows-universal-samples/bluetoothrfcommchat/)。示例验证 API 使用模式；本项目仍需做 Windows 与这台 Android 平板的互通测试。

### 2.3 PAN

微软文档要求另一台设备支持热点，并开启蓝牙网络共享。Windows 菜单出现“加入 PAN”仅说明存在该连接入口。PAN 建立后，可以复用本项目的 IP 传输通道。

依据为 [在 Windows 中连接蓝牙网络](https://support.microsoft.com/en-gb/windows/hardware/bluetooth/connect-to-a-bluetooth-network-in-windows)。本次读取的 Windows `Bluetooth Device (Personal Area Network)` 适配器状态为 `Disconnected`。设备名称出现在 Windows PnP 记录，不能据此认定业务连接已经建立。

### 2.4 局域网

Android 的 NSD 支持在本地网络发现服务。PC 和平板能够互相访问时，可以直接建立加密 TCP 连接，不需要云端中继。自动发现失败与 TCP 不可达是不同问题，因此需要二维码或手工地址回退。

依据为 [Android 网络服务发现](https://developer.android.com/develop/connectivity/wifi/use-nsd)。

### 2.5 AOA

Android Open Accessory 提供 USB 配件模式，但官方明确说明硬件支持并非所有设备都有。PC 端还涉及 USB 接口访问、驱动绑定和设备重新枚举。其可行性不能仅从 Android 版本或 USB-C 接口推出。

依据为 [AOA 协议概述](https://source.android.com/docs/core/interaction/accessories/protocol) 和 [Android USB accessory](https://developer.android.com/develop/connectivity/usb/accessory)。用户已经接受 ADB，因此首版无需引入 AOA。

## 3. Codex 接入证据

官方 [Codex app-server 文档](https://developers.openai.com/codex/app-server/) 提供结构化会话、增量事件和审批交互；[高级配置](https://developers.openai.com/codex/config-advanced/) 中的 `notify` 当前用于回合完成通知，单靠这一配置不能覆盖本项目的全部交互。

本机读取到 `codex-cli 0.166.0`，已完成以下实际验证。

1. 使用本机 CLI 导出 JSON Schema。
2. 从 Schema 读取具体方法名和必填字段。
3. 启动独立 `codex app-server --listen stdio://` 子进程。
4. 成功完成 `initialize` / `initialized` 握手并正常退出。

过程未创建会话、未启动模型回合、未执行 Codex 工具、未修改登录或配置。可复核结果见 [local-codex-evidence.json](../research/local-codex-evidence.json)。

重要的版本差异是，本机 `item/tool/requestUserInput` 参数具有必填 `isBlocking`；`autoResolutionMs` 已标记弃用，不能机械照抄网页旧示例。该请求仍标记为实验性。实施时需要冻结本机版本对应的 Schema，并以实际能力决定 UI 行为。

当前 CLI 的 `--remote` 与 `app-server proxy` 入口确实存在。官方同时把 app-server 的 WebSocket 传输标记为实验性且不受支持。因此，原版 Codex TUI 的透明代理作为独立兼容项验证，首版可靠路径采用本项目持有的 stdio 连接和受管理前端。

## 4. 可借鉴的案例

| 案例 | 原项目已经实现的相关能力 | 本项目可以采用的思路 | 需要自行完成的部分 |
| --- | --- | --- | --- |
| [scrcpy](https://github.com/Genymobile/scrcpy) | Android 经 USB / TCP/IP 与桌面交互，提供 Windows 客户端 | ADB 设备选择、连接生命周期和本地部署 | 终端数据、Codex 状态与审批；scrcpy 的投屏方向和业务不同 |
| [KDE Connect](https://kdeconnect.kde.org/) | 跨设备配对、通知与远程命令 | 设备管理、逐项授权、连接状态界面 | Codex 会话语义和精确审批 |
| [ttyd](https://github.com/tsl0922/ttyd) | 把终端输入输出提供给 Web 客户端 | 终端流、输入与窗口尺寸分离 | Windows 宿主和本项目的移动端审批模型 |
| [xterm.js](https://xtermjs.org/) | 被 VS Code、ttyd 等用于终端渲染 | 可按需用于原始终端页面 | 它不提供进程托管和命令完成识别 |
| [微软 RFCOMM 示例](https://learn.microsoft.com/en-us/samples/microsoft/windows-universal-samples/bluetoothrfcommchat/) | 自定义蓝牙服务上的双向聊天数据流 | RFCOMM 服务发布、连接与读写 | Android 互通、鉴权、重连和业务协议 |
| [OpenAI Codex Remote 工作流](https://developers.openai.com/blog/mastering-codex-remote-for-engineering) | 移动端工程任务、审批和完成通知 | 移动审批内容及任务切换的交互设计 | 本项目要求的本地 USB / RFCOMM 通道仍需自行实现与验收 |

案例说明组成技术有实践基础，不等同于这些项目已经完整实现了本产品。

## 5. 客户端技术依据

[WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/) 适合 Windows 原生界面。[.NET 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) 显示 .NET 10 为在支持期内的 LTS，因此方案采用 .NET 10，不依赖刚发布的大版本。

[Tauri](https://tauri.app/reference/webview-versions/) 使用系统 WebView，Windows 上是 WebView2。它是可行备选；实际内存、CPU 与启动时间必须测量，不能由包体大小直接推出。首版限定 Windows，采用 WPF 可以减少桌面主界面对 WebView 的依赖。

[Windows ConPTY](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session) 为程序托管终端输入输出提供基础。它应从会话创建时接入；不能把“枚举到一个 Windows Terminal 窗口”当成“获得其中任意标签页的完整双向终端流”。

Android 前后台连接生命周期遵循 [前台服务类型](https://developer.android.com/develop/background-work/services/fgs/service-types) 的要求。首版验收在 App 前台完成，后台服务作为后续扩展，不承诺 ZUI 任意省电状态下的持续运行。

## 6. 验证结论的层次

| 层次 | 本次覆盖 | 可以得出的结论 |
| --- | --- | --- |
| 官方文档与原项目案例 | USB、RFCOMM、PAN、LAN、Codex、ConPTY、客户端框架 | 架构存在公开实现路径 |
| 本机协议检查 | Codex 0.166.0 Schema 与 stdio 握手 | 本机已具备拟使用的 Codex 接入基础 |
| 真机 USB 数据测试 | 已授权 USB 设备、临时端口映射、双向数据校验 | 这台平板与 PC 的 USB 双向通道可用 |
| Windows 蓝牙检查 | 蓝牙设备记录、PAN 状态 | 可以识别设备记录，尚未验证 RFCOMM 互通 |
| 完整产品联调 | 尚无 Android APK 与 PC 业务程序 | 提醒、审批和全部通道的稳定性需要开发原型验收 |

USB 检查结果已记录在技术方案和本机验证记录中。后续真机验证通过一项，就更新一项，不把“文档支持”写成“已经实测通过”。
