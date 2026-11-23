# Code Link 远程连接方案与验证

本方案用于 Windows 电脑与 Android 手机／平板。任务、终端和授权状态仍由用户的电脑管理；手机只安装 Code Link。USB、蓝牙和局域网继续保留。

## 设计决策

2.0 使用 iroh 1.5.0。它提供按公钥寻址的加密双向流、NAT 穿透以及公共中继回退。公共中继免费、共享且限流，不提供可用性保证。Windows／Android 的实测结果见下方验收记录。

参考 [DSH Tether](https://github.com/zexadev/dsh-tether) 的独立网络组件、持久设备身份、连接关闭处理和强制中继验证方法。Code Link 保留自己的界面、消息帧、证书绑定、配对、授权与提醒逻辑，不采用 DSH 的界面、HTTP Cookie 代理或审批协议。

连接路径为 Android 原生连接层 → 内嵌 Rust 通信库 → iroh 直连或公共中继 → Windows 通信库 → 仅监听回环地址的 Remote TLS 入口 → Broker。

通信库只转发指定 Code Link 端口，不提供任意端口代理。现有 TLS 证书指纹和设备密钥验证继续生效；本机管理命名管道不对远程组件开放。网络模块的长期身份保存在系统保护的存储中，配对邀请只包含公钥和短期凭据。

## 生命周期

- Windows 用户开启远程访问后，Broker 启动网络模块。收起桌面窗口保留会话；关闭远程入口撤销当前远程连接。
- Android 前台服务拥有连接，Activity 只订阅显示。更换网络时通知网络模块并恢复连接；主动断开后不自动恢复。
- 连接中断不停止电脑任务。重新连接先同步最新状态；不自动重放未确认的终端输入或审批。
- 对端身份与连接实例分开记录，旧连接结束不能移除新连接。
- 后台省电限制、强行停止、电脑睡眠和公共中继不可达都会影响实时提醒。离线期间的完整历史记录属于独立的数据保留能力，当前有限终端缓冲不宣称提供无限历史。

## 可行性门槛

1. Windows 原生库与 Android ARM64 原生库均可重复构建。
2. 实际 PAD 与 Windows 双向传输已知内容，校验字节与连接身份。
3. 两端禁止 IP 直连后仍能经公共中继传输，并记录实际路径；USB 不存在业务端口反向映射。
4. 原生生命周期支持取消、重连和关闭；错误身份及错误 TLS 指纹被拒绝。
5. 集成后的 App 能列出终端、收到输出、提交输入与提醒。
6. PAD 改接手机热点、电脑保留校园网，执行跨网验收。仅同一局域网连通不算此项通过。
7. 拔线、非充电熄屏、网络切换与持续连接分别记录，短时测试不宣称完成全天候可靠性验收。

## 验证状态

方案已通过 Windows 与 Android ARM64 真机验证并接入应用。结果记录在 [远程验收](../research/remote-acceptance.json) 和 [鉴权验收](../research/remote-security-acceptance.json)。2026-11-23 的测试覆盖了公共中继强制转发、PAD 切换手机热点后的重连、实际输入回显，以及拔下 USB 后熄屏接收完成通知。后台提醒由用户实际确认。

此次验证属于短时功能与恢复验证；尚未完成 8 小时或整夜持续连接验收。免费公共中继的限流和可用性不能由应用保证。Android 被强行停止或受到系统后台限制时，实时提醒也可能暂停。

## 使用

1. Windows 打开 Code Link 的设备配对窗口，点击“开启远程连接”，等待远程入口就绪。
2. Android 点击“连接电脑 → 扫描二维码”，扫描**远程**二维码。也可复制并粘贴远程配对信息。
3. 初次邀请有效期为 10 分钟。过期后点击“刷新远程配对码”。已配对的设备使用长期设备凭据重新连接，无需每次扫码。
4. 手机可换用移动网络或热点。电脑需要保持开机、不睡眠，Code Link 代理保持运行。
5. 后台提醒使用现有前台服务；允许通知，并在需要时按系统提示允许后台运行。主动断开连接后不会自行恢复。
6. 关闭电脑端远程连接会停止远程入口、断开远程设备并轮换临时配对凭据。撤销某个设备的授权会拒绝该设备重连。

2.0 发布包包含 Windows x64 与 Android ARM64 通信库；Apple 客户端尚未接入这条远程路径。USB、蓝牙、局域网仍使用原有入口。

## 移动端查看与滚动

Android 终端内容区支持单指上下滑动：手指下滑查看更早的内容，上滑查看后面的内容。

右上角“全屏”把当前终端切换为横屏，隐藏任务列表和常规详情，扩大内容区。左上角“返回”或系统返回键恢复原布局和方向；会话、WebView 和输入草稿保留，全屏中的“输入”可展开键盘，“回到最新”继续可用。

电脑处理授权或问题后，新的运行状态会清除手机上的待处理红点、提示条和系统通知。断线或画面暂不可识别不代表请求已解决。完成提醒继续沿用 A/B 未读规则。实机记录见 [全屏与提醒验收](../research/phone-ui-acceptance.json)。

- **滑动：同步电脑**：已有 Windows Terminal 窗口默认使用此模式，需要该设备具有控制权。普通命令行通过窗口的历史滚动条定位；全屏程序没有历史滚动条时，向这个 Windows Terminal 窗口发送标准鼠标滚轮消息，由程序响应。PAD 接收电脑当前可见区域；滚动不会移动系统鼠标或抢占窗口焦点。
- **滑动：本地查看**：只在 PAD 上翻阅已同步的文本，不改变电脑窗口。回看时新输出保留阅读位置。只读设备使用这个模式；全屏程序未公开的旧内容需要通过同步电脑模式翻阅。
- **点击终端内容**：同步电脑模式下，短按映射到相同文本行列。支持 Windows Terminal 单窗格中已开启鼠标交互的终端程序；包含截图所示的 `New activity · ↓ Back to bottom · esc`。滑动、长按和本地查看不会发送点击。
- **回到最新**：普通终端定位到历史底部，本地查看恢复跟随输出。全屏程序公开 `Back to bottom` / `Back to latest` 提示时，快捷按钮会点击该提示；其余全屏程序仍需使用自身支持的滚动方式。
- `clink terminal` 创建的终端直接浏览移动端 xterm 缓冲，最多保留 5,000 行。窗口观察文本上限为 120,000 字符；这两种方式均不是永久日志存储。

当前同步滚动验证范围是 Windows Terminal 单窗格。全屏程序需要支持鼠标滚轮，并保持窗口还原；多窗格和不公开滚动接口的窗口会提示改用本地查看。权限会在操作排队和桌面执行前分别检查，切换会话或断线会清除尚未发送的滑动请求。

点击会在代理、桌面窗口和控制台投递前校验对应文本行，画面变化后提示重新点击。VT 输入程序接收 SGR 鼠标按下／松开序列，Win32 输入程序接收鼠标事件；不移动系统鼠标，不抢占窗口焦点。手机和平板使用同一套交互。手机移动数据验收见 [手机终端验收](../research/phone-terminal-acceptance.json)。

真实 PAD 的跨网滑动结果见 [滚动验收](../research/scroll-acceptance.json)。普通历史滚动和全屏程序鼠标事件均通过公共中继验证；USB 仅用于注入测试手势和读取调试结果。

## 直连与中继

这里的“直连”是互联网点对点传输：PAD 与电脑交换网络地址，尝试 UDP NAT 穿透，建立加密 QUIC 通道。两端可以处于不同城市、不同 Wi-Fi 或移动网络中，不要求蓝牙距离或同一局域网。中继可用于初始联络；直连成功后业务数据在两端之间传送。

网络限制导致穿透失败时，路径变为 PAD → 公共中继 → 电脑，中继转发加密数据。两种路径都由电脑执行任务；是否直连由实际网络条件决定。参见 [iroh NAT 穿透说明](https://docs.iroh.computer/concepts/nat-traversal)。

## 构建与复用工具链

本机已复用相邻 `Campus-Recruitment-Progress-Record/.toolchain` 中的 Rust 1.100.0、MSVC 与 Windows SDK。脚本仅读取这套环境，Cargo 缓存、Android 标准库、NDK、临时文件和输出均放在 Code Link 的 `.tools` / `.local` 下。也可以设置 `CODELINK_TOOLCHAIN_ROOT` 指向同样布局的工具链，或使用已配置好的系统 Rust / MSVC 环境。

```powershell
# 先按 BUILDING.md 准备 .NET、JDK 和原有 Android 工具。
./scripts/setup-remote.ps1
./scripts/build-remote.ps1 -Platform All
./scripts/build-desktop.ps1 -SkipNativeBuild
./scripts/build-android.ps1 -SkipNativeBuild
```

验证采用 Rust 1.100.0、iroh 1.5.0 与 NDK r27c，依赖版本锁定在 `native/remote/Cargo.lock`。Rust 标准库按官方 manifest 的 SHA-256 校验，NDK 按官方 repository metadata 的 SHA-1 校验。Android 库使用 16 KB ELF 页对齐，APK 也执行 16 KB zipalign。`-SkipNativeBuild` 用于复用已构建的本项目原生库；常规构建默认重新检查原生编译输入。

```powershell
. ./scripts/remote-env.ps1
cargo test --manifest-path native/remote/Cargo.toml --locked --target x86_64-pc-windows-msvc
python scripts/remote-probe.py --dll .local/remote-native/windows/codelink_remote.dll
```

`native/remote/probe/android` 是独立测试 APK 的源码，用于验证 JNI 与公共中继。业务测试使用独立 `remote-acceptance` 代理和仅用于回显的 Python 进程。调试时可设置 `CODELINK_REMOTE_RELAY_ONLY=1` 强制电脑端禁用 IP 直连；正常启动不设置该变量，让 iroh 自动选择路径。

## 安全边界与后续改进

- 公共中继只能转发加密流。iroh 验证端点公钥，应用层继续验证电脑 TLS 证书指纹和设备凭据。
- 远程首次配对使用独立 256 位随机凭据，近距离的六位配对码不能用于远程首次授权。握手有时限，连接并发有上限。
- Windows 长期身份由 DPAPI 加密保存，Android 长期身份由 Android Keystore 保护。每次网络连接拥有独立句柄，切网或停止连接会注销回调并关闭旧句柄。
- 后续可靠性验收应覆盖至少 8 小时、非充电 Doze、家庭宽带与不同移动运营商、网络反复切换、电脑断网恢复。可增加重连耗时与中继流量统计，按测得的数据调整重试与省电策略。
- 免费公共中继没有 SLA。未来可替换中继，继续使用本项目的业务协议和设备授权；本版无需账户、付费套餐、公网 IP 或额外服务器。

## 上游资料

- [iroh](https://github.com/n0-computer/iroh)，MIT / Apache-2.0。
- [iroh 中继与公共服务边界](https://docs.iroh.computer/concepts/relays)。
- [DSH Tether](https://github.com/zexadev/dsh-tether)，仅作设计参考。
- [Android Doze 与后台网络](https://developer.android.com/training/monitoring-device-state/doze-standby)。
