# Code Link 当前实现与使用

更新于 2026-11-23，对应 Code Link 2.0.0。Windows 包含运行时；Android 提供中文与英文 APK。安装步骤见 [安装指南](INSTALL.md)，互联网连接、横屏、滑动和点击的范围见 [远程设计](REMOTE.md)。

## 使用

1. 启动 `dist/desktop/CodeLink.exe`。主窗口左侧是一份命令行列表，下方选择移动设备，右侧查看选中终端的实际文本。
2. 远程使用时，在 Windows 开启远程入口，Android 扫描远程二维码。桌边也可开启 USB 调试并授权数据线连接，使用“连接设备 → USB 一键连接”；蓝牙和 LAN 保留。
3. PC 可选中命令行和在线平板，点击“映射到移动端”；平板也会显示电脑扫描到的完整窗口列表，点击列表中的窗口即可映射并打开。已配对设备可主动选择本机可识别的终端。
4. 平板输入文本并发送，也可使用 Enter、Esc、Tab、方向键、Ctrl+C 响应实际终端提示。PC 可以照常在原命令行输入。
5. 关闭桌面面板会收进托盘。双击托盘图标、点击“打开面板”或再次启动程序，都恢复同一个面板。

现有窗口映射跟随该 Windows Terminal 窗口的活动标签页。两个控制台使用相同标题、目标权限高于 Code Link，或终端未提供标准 Windows 控制台接口时，输入可能被拒绝并显示原因。程序不向无法唯一定位的窗口发送输入。

## 架构

| 模块 | 当前代码与实现 |
| --- | --- |
| 桌面面板 | C# / WPF，系统托盘，单实例唤回；没有 Electron 或桌面 WebView |
| 本地代理 | 独立 .NET 进程；当前用户命名管道；配对、映射、状态与输入确认 |
| 外部命令行 | 进程与窗口扫描；UI Automation 文本快照；独立 ConsoleBridge 使用 AttachConsole / WriteConsoleInputW 投递按键 |
| 受管理终端 | `clink terminal` 通过 ConPTY 启动真实命令行，本地和移动端均可输入 |
| Codex 接入 | `clink codex` 使用本机 app-server，处理结构化授权、问题、输出与回合结束 |
| Android | Java 原生 View，Android 蓝牙、通知和密钥库；本地 WebView 显示终端文本，ConPTY 使用 xterm.js |
| 互联网远程 | 内嵌 Rust / iroh 1.5.0，QUIC 直连或公共中继；应用层保留 TLS 绑定与设备鉴权 |
| USB | ADB reverse + TLS，检测并修复缺失的当前业务端口映射 |
| 蓝牙 | 配对后的加密 RFCOMM 和应用设备鉴权；不需要 PAN |
| 局域网 | TCP / TLS、二维码或连接信息；已实现，当前网络下尚未完成真机直连验收 |

桌面每 5 秒扫描窗口，映射窗口约每秒读取一次内容。映射过程自动通知移动端切换对象。窗口文本保留换行和空格，平板不再次按错误列宽重排。当前外部窗口同步的是终端公开的文本，不包含原终端配色、光标图形或截图。缓冲上限 120,000 字符，并非无限历史记录。

外部窗口采集和输入连接器依赖桌面托盘进程存活；收起面板可以继续使用，显式退出面板会停止外部窗口更新和按键投递。由代理管理的终端继续运行。重新启动代理后，需要重新映射外部窗口，避免把过期窗口句柄当作有效目标。

## 命令行入口

在项目根目录运行以下命令，可真正打开指定目录的终端。

```powershell
.\dist\desktop\clink.exe terminal --cwd D:\project --name 项目终端 --new-window
.\dist\desktop\clink.exe codex --cwd D:\project --name 项目助手 --new-window
.\dist\desktop\clink.exe scan
```

`terminal` 默认运行 PowerShell，可在 `--` 后指定程序和参数。`codex` 提供 Code Link 自己的文本前端。当前桌面无需“创建会话”表单，用户在命令行中启动即可。已有原版 Codex 窗口也能走外部窗口映射。

## 授权与提醒

移动端左栏提供 A / B 标签页。新命令行默认 B；选中后点击右侧的“A 模式 / B 模式 ⇄”切换归属，点击“重命名”设置本平板显示名。名字和分组在本机保存，外部窗口按电脑身份、进程和窗口句柄区分。重命名不修改 Windows 终端标题。

- A 组：每个任务完成分别提醒。
- B 组：本轮实际运行过的任务全部完成后汇总提醒；未启动的空闲窗口不计入；尚未点开的窗口也同步状态，识别到运行后即纳入。失败、中断、断线或状态未知不能算成功完成。新一轮运行后重新统计。
- 等待授权或用户回答：无论在哪组都立即提醒。系统通知及提示音沿用通知权限和系统设置。
- 完成提醒的卡片红点、红色边框、标签页红点和提示条保留到用户确认已读。授权和问题在电脑处理后，收到恢复运行状态便自动清除对应提示与系统通知；未知或断线不当作已解决。B 组部分完成时不显示完成红点，全部完成后才同时标红 B 标签、汇总行和本轮参与的卡片。点击对应卡片或“点击确认提醒”会清除该终端的红点；最后一个完成红点清除时，B 标签、汇总行和对应系统完成通知一起清除。汇总行也可以一次确认本轮全部完成提醒。单个授权或失败提醒不染红 B 标签，汇总确认不会清除这些提醒。B 组开始新一轮时清除上一轮的完成标记。确认已读不等于允许执行，授权按钮或原终端回答仍需另外操作。
- 名称、分组、未读事件与 B 组参与任务会在 App 重启后恢复；重复状态和同一轮完成不反复弹提醒。

外部终端中的选择题、确认提示仍可通过文本、方向键和 Enter 操作。目前增加了针对已测试 Codex TUI 的画面识别：运行指示、完整授权对话，以及明确的 `Worked for` 结束标记和输入区组合。完成标记要连续两次读取才生效；原生 `choice [Y,N]?` 等确认提示也可识别。窗口内容超过 12 秒未更新采样，状态变为待同步。

画面识别属于特定终端版本适配，不能从任意命令行的静默推断完成。没有匹配到明确状态时显示“任务状态待识别”，不触发成功汇总。短回合未显示结束标记、其他 CLI、终端滚到历史位置或改变 TUI 布局的情况，仍需继续适配。可靠的业务事件优先使用下述结构化模式。受管理的有限进程则使用真实退出码，0 为完成，非 0 为失败。

`clink codex` 使用结构化协议处理命令执行、文件修改、权限请求、用户问题及 MCP 表单。平板显示待处理卡片，回答回到原请求；回合结束、失败和中断可产生通知。

### 后台连接与横幅（Android 1.2.0）

连接启用后，`LinkService` 使用 Android 的 `connectedDevice` 前台服务维持所选连接；通知栏显示安静的“Code Link · 后台连接”，其中可以直接断开连接。页面退到后台、被回收或从最近任务中移除时，不再关闭连接。服务使用 `START_STICKY` 请求系统回收后重建，重建后重新认证和同步；用户主动断开则停止服务并释放唤醒锁，重新打开页面也不会自行恢复这次主动断开的连接。

`LinkRuntime` 统一持有连接、完整终端目录、所选终端输出缓存和提醒状态。Activity 仅在可见时订阅；再次打开时先收到当前完整输出，再接收增量。后台缓存沿用 120,000 字符上限。A/B 判断、名称和分组与前台共用，授权提醒继续独立触发。完成事件通过高优先级的“任务完成横幅”通知通道发送，点击单条通知打开对应终端，点击 B 汇总进入 B 页，查看完全部成员后自动撤回完成通知。

在“后台与通知”中检查通知权限、完成横幅、电池优化和勿扰状态。测试平板已通过系统授权允许后台运行。ZUI 还需允许应用后台运行及悬浮通知；系统勿扰、用户关闭横幅或锁屏隐私设置会决定实际展示方式。熄屏时通知留在系统通知中心，点亮后按系统锁屏规则显示。应用不会绕过安全锁屏，也不会自动关闭勿扰。强行停止 App、手动停止前台服务或物理链路断开仍会停止实时接收；物理连接恢复后沿用退避重连。

技术依据：[Android 连接设备前台服务](https://developer.android.com/develop/background-work/services/fgs/service-types#connected-device)、[通知通道优先级](https://developer.android.com/develop/ui/compose/notifications/channels)、[Doze 与电池优化限制](https://developer.android.com/training/monitoring-device-state/doze-standby)。旧“任务完成”通道的优先级不能由应用直接修改，因此 1.2.0 使用独立的高优先级完成横幅通道；用户之后对该通道的设置会被保留。

后台真机测试使用真实有限 PowerShell 任务，记录在 `research/pad-background-acceptance.json`；横幅画面见 `research/a-banner.png`、`research/b-banner.png`。测试脚本为 `scripts/test-pad-background.py`；提醒状态与输出缓存分别有纯 Java 行为测试。长时间耗电、系统强回收后的各厂商行为以及蓝牙/局域网锁屏连接仍需专项持续验证。

结构化事件依据：[Codex app-server 官方文档](https://learn.chatgpt.com/docs/app-server)，本次核对了回合结束的 completed / interrupted / failed 状态和服务端授权请求。窗口识别不冒充这些协议事件。

本次新增验收：`research/pad-reminder-acceptance.json` 记录三条真实 PowerShell 任务的 A 单独提醒、B 全部完成、已读确认、重命名及重启保留；`research/pad-window-alert-acceptance.json` 记录平板主动连接扫描窗口、未打开窗口的真实 choice 提示红点及回答，并断言授权不把 B 标签标红；`research/b-group-mark-acceptance.json` 检查先完成一条 B 不红、全部完成后显示完成红点、逐个查看清除卡片红点、最后一个已读时自动清除 B 标签及汇总行，并验证部分已读与全部已读均能在 App 重启后保留。`research/activity-acceptance.json` 记录窗口识别样例回归，`research/catalog-parity-acceptance.json` 记录两端列表核对。Android 的独立提醒状态机测试位于 `android/tests/ReminderStateTest.java`。

## 连接与故障处理

平板使用同一设备身份切换 USB 和蓝牙。服务端保留映射，重新连接后发送最新状态；客户端没有把未确认输入自动重放到终端。重复操作 ID 只执行一次。现有窗口输入须收到连接器写入确认后才显示成功。

USB 检测兼容本机 ADB 5037 / 5038 两个端口，使用 `-d` 识别物理 USB，避免 Windows ADB 输出没有 `usb:` 字段时误判没有平板。每 5 秒检查一次当前业务端口的 reverse 映射。

蓝牙和网络使用退避重连，服务端约每 8 秒发送心跳状态。蓝牙连续 25 秒未收到完整消息时关闭旧连接并重试。连接生命周期有日志，便于区分安装更新、主动切换、超时和真正的异常断开。

已配对凭据在 Windows 使用 DPAPI、Android 使用 Keystore 保护。USB / LAN 校验配对时的证书指纹；本机命名管道仅限当前 Windows 用户。局域网默认不接受外部连接，开启后需要两端 TCP 可达。

## 本轮修复与验证

| 检查 | 结果与证据 |
| --- | --- |
| 当前 Codex、最小化窗口扫描、同进程多窗口 | 扫描回归通过，见 `research/scan-acceptance.json` |
| 统一命令行列表、映射可用、右下角状态、真实文本 | UI 自动化通过，见 `research/desktop-ui-acceptance.json` |
| 托盘恢复黑屏 | 测试曾用 Win32 显示隐藏 WPF 窗口外壳，内容仍隐藏；现通过 WPF Show 恢复，重复启动唤回已有实例，已回归 |
| USB 与蓝牙外部命令行输入 | 真机输入标记出现在真实 cmd 输出，见 `research/external-mapping-acceptance.json` |
| 外部终端确认 | 平板完成真实 `choice /c YN` 提示的 Y 选择 |
| 最小化终端和面板收托盘 | 平板输入 `MINIMIZED_PAD_OK` 并收到返回 |
| USB 映射缺失 | 删除当前测试端口 reverse 后，工具自动恢复，平板重新连上 |
| 蓝牙连续连接与重连 | 一次连续约 8 分钟，无非人为断开；点击重连后映射仍在，不代表长时间稳定性验收 |
| 大窗口文本 | 超过 80,000 字符的完整快照传输回归通过；修复蓝牙 8,000 字符裁切及平板错误换行 |
| 权限与重复操作 | 未映射拒绝、错误证书拒绝、重复输入只执行一次、取消映射撤销输入均通过，见 `research/transport-acceptance.json` |
| Codex 结构化授权 | 实际 MCP 权限与确认表单由平板提交，任务完成，见 `research/codex-approval-acceptance.json` |

本轮尚未完成原方案的 8 小时浸泡测试、LAN 真机直连、iPad、WSL 兼容、mDNS 自动发现或全部 Codex 授权类型的真机覆盖。Android 后台连接与横幅已完成上述 USB 真机验收；更多厂商后台策略、蓝牙锁屏和长时间耗电仍需专项验证。外部窗口状态识别的具体适用范围见上文。

## 构建与交付

```powershell
.\scripts\build-desktop.ps1
.\scripts\build-android.ps1 -Install
dotnet build desktop/CodeLink.Smoke/CodeLink.Smoke.csproj -c Release
dotnet desktop/CodeLink.Smoke/bin/Release/net10.0/CodeLink.Smoke.dll --transport
```

Windows 构建需要 .NET 10 SDK；当前机器使用已安装的 .NET 10 预览 SDK，产物依赖 .NET 10 Desktop Runtime。Android 使用本机 Java 21 和 `.tools` 内 Android SDK 工具，minSdk 28 / targetSdk 34，APK 为本地测试签名，不包含云端依赖。指定 JPG 图标和 GIF 启动画面已经打包。

保留完整 Windows 输出目录，不要单独复制 exe。`runtime.json` 指定实例名和本地状态目录；本机联调使用 v2 实例，独立分发包使用自身 state 目录。不要把 `.local` 的设备密钥和证书复制给其他电脑。
