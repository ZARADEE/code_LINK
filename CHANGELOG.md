# 版本记录

## v1.4.0 — 2026-11-15

- Windows English and Chinese packages now include the .NET Desktop Runtime.
- Guided USB pairing shows progress, online-device count and actionable errors in the connection window.
- USB setup now waits for the Android app to authenticate the current attempt before reporting success; the pairing QR code can be refreshed.
- Android uses a terminal list/detail flow on narrow phone windows and two panes on tablets with enough space. Rotation and window resizing preserve per-terminal input drafts.
- Phone keyboard layouts keep input and Send visible; terminal shortcuts scroll horizontally and settings dialogs scroll vertically.
- Windows packages use a fresh publish manifest to exclude stale runtime files and machine state.
- English and Chinese introductions explain the target user, concrete workflows and platform boundaries; the site adds a complete workflow guide.
- Apple CI uses isolated, headless simulators while preserving real iPhone/iPad pairing and terminal checks.


## 1.3.0 · 2026-11-14

- Windows 与 Android 新增英文发布包，覆盖界面、通知、设置、错误提示与 CLI 帮助；所有应用包名标注 `zh-CN` 或 `en`。
- 两种 Android 语言包沿用相同签名和应用 ID；蓝牙设置兼容旧值，授权失效使用稳定错误码。
- 新增 `if_want_learn.md`，解释技术栈、消息流程、核心模块、语言构建与二次开发注意事项。

- 新增 macOS SwiftUI 客户端，支持 Apple silicon 与 Intel，内含 .NET 运行时。
- macOS 使用原生 zsh PTY，可创建终端与 Codex 会话、配对移动设备和分配控制权。
- 新增 iPhone / iPad SwiftUI 客户端，提供二维码配对、证书固定 TLS、终端输入输出、审批、问题回答和 A/B 提醒。
- Apple 凭据存入 Keychain；iOS 进入后台后暂停连接，回到前台重新连接，操作不会自动重放。
- 增加 Apple 编译、协议与提醒行为检查、真实 PTY / TLS / iOS 模拟器联通验证。
- 发布 macOS 应用、iOS 模拟器包、未签名真机归档与 Xcode 源码工程。当前没有 Apple Developer 账号，不提供公证、签名 IPA 或 TestFlight。

## 1.2.0 · 2026-11-13

- Android 前台服务持续接收本地终端状态与输出，页面退到后台或回收后继续连接。
- A 组逐个完成、B 组全部完成时发送系统横幅，支持点击通知返回。
- 增加后台与通知设置入口，显示横幅、勿扰和电池优化状态。
- 验证 USB 后台、熄屏通知和解锁后的完整输出恢复。

## 1.1.1 · 2026-11-13

- B 组完成卡片支持逐个确认，最后一个已读时自动清除组标签及系统通知。
- 已读状态在 App 重启后保留。

## 1.1.0 · 2026-11-13

- A/B 标签分组、默认 B、终端重命名和待处理红点。
- 平板同步完整扫描目录，支持主动打开尚未映射的终端。

## 早期开发

Windows 原生托盘面板、窗口扫描与映射、移动端输入、USB / 蓝牙传输，以及 Codex 结构化授权。细节见[开发记录](docs/development-status.md)。
