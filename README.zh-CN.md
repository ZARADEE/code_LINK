<p align="center"><img src="docs/assets/logo.png" width="88" alt="Code Link 图标"></p>
<h1 align="center">Code Link 2.0</h1>
<p align="center"><strong>电脑运行任务，手机和平板随时查看、操作与接收提醒。</strong></p>
<p align="center">互联网远程 · 横屏终端 · 滑动与点击 · Codex 授权 · A/B 任务提醒</p>
<p align="center"><a href="README.md">English</a> · <strong>简体中文</strong></p>
<p align="center">
<a href="https://github.com/ZARADEE/code_LINK/releases/latest"><img src="https://img.shields.io/github/v/release/ZARADEE/code_LINK?label=release&amp;color=52745b" alt="最新发布版本"></a>
<a href="https://github.com/ZARADEE/code_LINK/actions/workflows/ci.yml"><img src="https://github.com/ZARADEE/code_LINK/actions/workflows/ci.yml/badge.svg" alt="构建与检查状态"></a>
<a href="LICENSE"><img src="https://img.shields.io/badge/code-MIT-52745b" alt="代码采用 MIT 许可"></a>
</p>
<p align="center"><a href="#downloads">下载安装</a> · <a href="#quick-start">开始连接</a> · <a href="#terminal">使用终端</a> · <a href="#reminders">A/B 提醒</a> · <a href="#faq">常见问题</a> · <a href="https://zaradee.github.io/code_LINK/">项目主页</a></p>

![Code Link 的 Windows 桌面面板与 Android 平板界面](docs/assets/overview.png)

Code Link 把 Windows 上的命令行接到 Android 手机和平板。构建、测试和 Codex 继续在自己的电脑上运行；你可以离开桌面查看输出、发送输入、处理授权，在任务完成时收到提醒。

**2.0 支持通过互联网连接自己的电脑。** 两端只需安装 Code Link，无需租服务器、购买套餐或在手机上安装 VPN。iroh 尝试建立加密直连，网络不允许时使用免费公共中继；USB、蓝牙和局域网继续保留。[2.0 更新记录](CHANGELOG.md#200--2026-11-23)

## 你可以用它做什么

| 电脑上正在发生的事 | 手机和平板上的操作 |
| --- | --- |
| 离开电脑后，构建或测试仍在运行 | 用移动数据或其他 Wi-Fi 查看终端，任务仍在自己的电脑上执行 |
| Codex 等待确认 | 回答原终端提示，或通过 `clink codex` 使用结构化授权卡片 |
| 需要翻看前面的输出 | 滑动带动电脑终端滚动，或切到本地查看；轻点 Back to bottom 回到底部 |
| 手机竖屏看不清命令行 | 点右上角“全屏”进入横屏；左上角返回恢复原布局和输入草稿 |
| 多个任务并行 | 给终端命名、分组，选择逐个提醒或整批完成后提醒 |
| 已在电脑上处理授权 | 手机收到最新状态后自动清除对应提示、红点和系统通知 |

![真实平板演示：发送输入、执行仓库检查、收到完成提醒](docs/assets/workflow.gif)

*动图来自中文客户端的 USB 真机操作，四帧展示输入、执行与完成，展示时间已缩短。[静态图](docs/assets/workflow-complete.png)。*

<a id="downloads"></a>
## 下载与平台

| 平台 | 2.0 下载 | 要求与支持范围 |
| --- | --- | --- |
| **Windows** | **[中文 ZIP](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-Windows-v2.0.0-zh-CN.zip)** · [English](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-Windows-v2.0.0-en.zip) | x64；已包含 .NET 10 桌面运行时，完整解压后运行 `CodeLink.exe` |
| **Android** | **[中文 APK](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-Android-v2.0.0-zh-CN.apk)** · [English](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-Android-v2.0.0-en.apk) | Android 9+、ARM64；手机与平板均支持 |
| macOS | [Apple silicon](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-macOS-AppleSilicon-v2.0.0-en.zip) · [Intel](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-macOS-Intel-v2.0.0-en.zip) | macOS 14+；英文界面，创建托管会话，经 LAN 连接 |
| iPhone / iPad | [Apple 安装指南](docs/APPLE.md) | iOS / iPadOS 17+ 开发预览；模拟器或 Xcode 自行签名，经 LAN 连接 |

Windows + Android 是目前完成真机验证的主要组合。Apple 客户端已通过构建与模拟器连接检查，尚未接入 iroh 远程、Windows 窗口滑动和点击；iOS 退到后台会暂停连接。Mac 应用为临时签名、未公证，使用 Mac 包无需 iPhone 签名。

完整文件与校验值见 [v2.0.0 Release](https://github.com/ZARADEE/code_LINK/releases/tag/v2.0.0)。从 1.x 升级时，Android 可覆盖安装官方 APK；Windows 替换应用文件前先退出 Code Link，并保留 `state` 目录。中英文 APK 共用应用身份和证书，可以覆盖切换语言。[安装与升级说明](docs/INSTALL.md)

<a id="quick-start"></a>
## 开始连接

先安装两端应用。电脑需要保持联网、开机且不睡眠，Code Link 保持运行。

### 互联网远程

1. Windows 打开**连接设备**，点击**开启远程连接**，等待远程入口就绪。
2. Android 打开**连接电脑 → 扫描二维码**，扫描电脑上的**远程二维码**；也可以粘贴远程配对信息。
3. 在 Android 选择终端，或在 Windows 选中终端和在线设备，点击**映射到移动端**。
4. 手机换用移动数据、热点或其他 Wi-Fi，在测试终端发送 `echo hello`，确认输出回到手机。
5. 需要后台提醒时，在 Android 的**更多 → 后台与通知**中允许通知和后台运行。

首次邀请有效期为 10 分钟，过期可刷新；已配对设备用保存的凭据重连。远程连接无需 USB 调试，跨网使用也无需一直插线。[连接原理与排查](docs/REMOTE.md)

### USB、蓝牙与局域网

| 方式 | 如何连接 | 适合什么情况 |
| --- | --- | --- |
| USB | 开启 Android USB 调试，接上数据线、解锁并允许电脑调试，然后在 Windows 点“USB 一键连接” | 桌边使用、充电和首次调试 |
| 蓝牙 | 系统配对后在 Code Link 选择蓝牙连接；扫码或粘贴配对信息，允许“附近的设备”，选择电脑 | 附近使用，不需要先连接 USB 或建立 PAN |
| LAN | Windows 开启局域网入口，Android 扫码或输入连接信息；两端 TCP 网络必须可达 | 同一局域网内使用 |

USB 和蓝牙保留原有本地连接方式。校园网隔离或运营商 NAT 影响直连时，可使用互联网远程入口。[详细安装步骤](docs/INSTALL.md)

<a id="terminal"></a>
## 使用终端

已有 PowerShell、命令提示符、Windows Terminal 和 Codex 窗口可以直接映射。手机使用列表／详情切换，宽屏平板并排显示列表和终端；显示名称与 A/B 分组在移动端保存。

| 操作 | 效果 |
| --- | --- |
| 下滑 / 上滑 | 查看前文 / 后文；“滑动：同步电脑”会带动电脑窗口滚动 |
| 切换“滑动：本地查看” | 只翻阅已经同步的文本，新输出到来时保留阅读位置 |
| 轻点终端内容 | 将短按映射到对应行列，操作支持鼠标交互的终端按钮 |
| 回到最新 | 普通终端回到历史底部；出现 Back to bottom / Back to latest 时触发该按钮 |
| 右上角全屏 | 横屏放大终端；可展开输入，点左上角返回或系统返回键恢复原布局 |
| 输入与快捷键 | 发送文本、Enter、Esc、Tab、方向键或 Ctrl+C |

同步滚动与点击当前支持 **Windows Terminal 单窗格**，需要设备拥有控制权。全屏终端程序还需要启用鼠标交互、电脑窗口保持还原；滑动、长按和本地查看不会发送点击。画面已变化时，点击会被拒绝并提示重试。

映射跟随 Windows Terminal 的**活动标签页**，多个任务独立查看时使用不同窗口。外部窗口同步文本和间距，不复现完整配色和图形光标，缓存上限约 120,000 字符。相同窗口标题、较高权限或不支持的控制台接口可能阻止输入，应用会显示原因。

<details>
<summary><strong>托管终端与结构化 Codex 会话</strong></summary>

在解压后的应用目录运行：

```powershell
.\clink.exe terminal --cwd D:\project --name 项目终端 --new-window
.\clink.exe codex --cwd D:\project --name 项目助手 --new-window
.\clink.exe scan
```

`terminal` 使用真实 PTY，终端视图最多保留 5,000 行。`codex` 使用本机 Codex app-server 提供结构化授权、问题与回合状态。原版 Codex 窗口也可直接映射，通过文字和快捷键操作。

</details>

<a id="reminders"></a>
## A/B 完成提醒

| 分组 | 什么时候提醒 | 怎么清除 |
| --- | --- | --- |
| A · 逐个 | 每个任务完成时分别提醒 | 查看该终端的提醒 |
| B · 汇总，默认 | 本轮实际运行过的任务全部成功完成 | 逐个查看参与任务，或确认汇总提醒 |

空闲且未启动的终端不阻塞 B 组；失败、中断、断线和未知状态不算成功。授权和问题在两组中都会立即提醒。电脑处理请求并恢复运行后，对应待处理提示自动消除；点击“已读”本身不会授权执行。

![真实 Android A/B 完成通知横幅](docs/assets/notifications.png)

完成判断取决于来源：有限托管命令使用退出码，`clink codex` 使用回合事件，已有终端使用已适配的画面标记。无法识别的输出保留未知状态，不会因为暂时没有新输出就报完成。

<a id="background"></a>
## 后台与远程连接

Android 前台服务持有连接和提醒；允许通知、横幅与后台运行后，可以切到其他 App 或熄屏。系统勿扰和厂商省电策略仍然生效。断线期间电脑任务继续运行，重连后同步最新状态，不自动重发尚未确认的输入。

iroh 直连指**跨互联网的点对点连接**，不要求蓝牙距离或同一 Wi-Fi。穿透失败时，公共中继转发加密数据，终端和任务仍由你的电脑管理。公共中继免费、共享且有限流，没有可用性保证。

已完成手机移动数据／平板热点与电脑校园网之间的输入、滑动、点击、横屏和授权提示清除测试；平板拔线熄屏后收到完成提醒。远程整夜、8 小时持续连接和跨运营商耗电测试仍在[后续计划](docs/ROADMAP.md)中。[远程验收](research/remote-acceptance.json) · [手机交互](research/phone-terminal-acceptance.json) · [全屏与提醒](research/phone-ui-acceptance.json)

<a id="faq"></a>
## 常见问题

| 问题 | 处理方式 |
| --- | --- |
| 远程需要付钱或租服务器吗？ | 本版使用自己的电脑和免费公共中继，无需套餐；两端需要网络 |
| 手机只想装 Code Link | 可以，远程模式不需要额外 VPN、代理或 USB 调试 |
| 电脑睡眠后还能访问吗？ | 不能，访问期间保持电脑开机、不睡眠、Code Link 运行 |
| 终端文字太小 | 点右上角“全屏”，横屏查看；退出保留当前会话与草稿 |
| 手机不能滚动或点击电脑内容 | 检查控制权、同步模式和 Windows Terminal 单窗格；全屏程序需支持鼠标操作 |
| USB 找不到设备 | 解锁、允许 USB 调试，检查数据线和端口；[安装排查](docs/INSTALL.md#usb-连接) |
| 没有完成提醒 | 检查通知与后台权限、A/B 分组和完成信号；B 组要等参与任务全部成功 |
| iPhone 能用相同远程功能吗？ | Apple 当前使用 LAN，iOS 需要自行签名且后台暂停；[Apple 指南](docs/APPLE.md) |

## 文档与参与

- [安装与升级](docs/INSTALL.md) · [远程连接与技术设计](docs/REMOTE.md) · [Apple 指南](docs/APPLE.md)
- [从源码构建](docs/BUILDING.md) · [实现说明](docs/implementation.zh-CN.md) · [源码导读](if_want_learn.md)
- [更新记录](CHANGELOG.md) · [开发记录](docs/development-status.md) · [后续计划](docs/ROADMAP.md)
- [报告问题](https://github.com/ZARADEE/code_LINK/issues/new/choose) · [讨论与使用反馈](https://github.com/ZARADEE/code_LINK/discussions) · [贡献说明](CONTRIBUTING.md)

欢迎提交带设备型号、连接方式和复现步骤的兼容记录。代码采用 [MIT 许可证](LICENSE)，图片、动画和第三方组件见[素材说明](assets/README.md)与[第三方声明](THIRD_PARTY_NOTICES.md)。

*展示图来自真实设备或标注过的模拟器。旧平板图片展示本地流程，2.0 手机交互以对应验收记录为准；[图片来源](docs/assets/README.md)。*
