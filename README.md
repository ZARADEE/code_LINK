<p align="center"><img src="docs/assets/logo.png" width="72" alt="Code Link icon"></p>
<h1 align="center">Code Link 2.0</h1>
<p align="center"><strong>Keep tasks on your PC. Follow and control them from your phone or tablet.</strong></p>
<p align="center">Internet remote access · Landscape terminal · Swipes and taps · Codex approvals · A/B task alerts</p>
<p align="center"><strong>English</strong> · <a href="README.zh-CN.md">简体中文</a></p>
<p align="center">
<a href="https://github.com/ZARADEE/code_LINK/releases/latest"><img src="https://img.shields.io/github/v/release/ZARADEE/code_LINK?label=release&amp;color=52745b" alt="Latest release"></a>
<a href="https://github.com/ZARADEE/code_LINK/actions/workflows/ci.yml"><img src="https://github.com/ZARADEE/code_LINK/actions/workflows/ci.yml/badge.svg" alt="Build and checks"></a>
<a href="LICENSE"><img src="https://img.shields.io/badge/code-MIT-52745b" alt="MIT code license"></a>
</p>
<p align="center"><a href="#choose-your-devices">Downloads</a> · <a href="#get-started">Get started</a> · <a href="#your-terminal-within-reach">Terminal controls</a> · <a href="#ab-completion-reminders">A/B alerts</a> · <a href="#troubleshooting">FAQ</a> · <a href="https://zaradee.github.io/code_LINK/en.html">Website</a></p>

![English Code Link on Windows and a physical Android tablet](docs/assets/overview-en.png)

Code Link connects Windows terminals to your Android phone or tablet. Builds, tests and Codex keep running on your own computer while you read output, send input, respond to approvals and receive completion alerts away from the desk.

**2.0 brings internet access to your own PC.** Install Code Link on both devices; no rented server, paid plan or mobile VPN is needed. iroh attempts an encrypted direct connection and falls back to a free public relay when necessary. USB, Bluetooth and LAN remain available. [2.0 changes](CHANGELOG.md#200--2026-11-23)

## What you can do

| While the PC works… | On your phone or tablet |
| --- | --- |
| A build or test continues after you leave | Follow its terminal over cellular data or another Wi-Fi network |
| Codex needs a decision | Answer the terminal prompt, or use structured cards with `clink codex` |
| You need earlier output | Swipe to scroll the PC or browse locally; tap Back to bottom to return |
| Terminal text is small on a phone | Open Full screen for landscape viewing, then return to the same session and draft |
| Several jobs run in parallel | Name and group terminals; receive individual or batch completion alerts |
| You handle an approval on the PC | Its mobile badge, banner and system notification clear when the new state arrives |

![Real tablet walkthrough: input, repository checks and a completion reminder](docs/assets/workflow.gif)

*Four real captures from the Chinese app over USB, with shortened display timing. [Still image](docs/assets/workflow-complete.png).*

## Choose your devices

| Platform | 2.0 downloads | Requirements and scope |
| --- | --- | --- |
| **Windows** | **[English ZIP](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-Windows-v2.0.0-en.zip)** · [简体中文](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-Windows-v2.0.0-zh-CN.zip) | x64; .NET 10 Desktop Runtime included. Extract everything and run `CodeLink.exe` |
| **Android** | **[English APK](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-Android-v2.0.0-en.apk)** · [简体中文](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-Android-v2.0.0-zh-CN.apk) | Android 9+, ARM64. Phones and tablets supported |
| macOS | [Apple silicon](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-macOS-AppleSilicon-v2.0.0-en.zip) · [Intel](https://github.com/ZARADEE/code_LINK/releases/download/v2.0.0/CodeLink-macOS-Intel-v2.0.0-en.zip) | macOS 14+; English UI, managed sessions over LAN |
| iPhone / iPad | [Apple setup guide](docs/APPLE.md) | iOS / iPadOS 17+ developer preview; simulator or personal Xcode signing, LAN connection |

Windows + Android is the main setup validated on physical devices. Apple builds and simulator connections are checked in CI; Apple clients do not yet include iroh remote access or Windows window swipes and taps. iOS pauses its connection in the background. Mac apps are ad-hoc signed and not notarized; using the Mac app does not require iPhone signing.

All files and checksums are in the [v2.0.0 release](https://github.com/ZARADEE/code_LINK/releases/tag/v2.0.0). Upgrading from 1.x: install the official APK over the existing app; close Code Link before replacing Windows files and preserve its `state` directory. Chinese and English APKs share the application identity and signing certificate. [Installation and upgrades](docs/INSTALL.en.md)

## Get started

Install both apps. Keep the PC online, awake and running Code Link.

### Connect over the internet

1. In Windows, open **Pair device**, enable remote access and wait for the endpoint to be ready.
2. On Android, choose **Connect to PC → Scan QR code** and scan the **remote** invitation. Pasting remote pairing details also works.
3. Choose a terminal on Android, or select a terminal and online device in Windows and choose **Share with device**.
4. Switch the phone to cellular data, a hotspot or another Wi-Fi network. Send `echo hello` to a test terminal and check the returned output.
5. For background alerts, open **More → Background & alerts** on Android and allow notifications and background operation.

The initial invitation expires after 10 minutes and can be refreshed. Paired devices reconnect with stored credentials. Internet mode requires no USB debugging or cable. [Remote setup and design](docs/REMOTE.md)

### Connect nearby

| Transport | Setup | Typical use |
| --- | --- | --- |
| USB | Enable USB debugging, attach a data cable, unlock and authorize the PC, then choose Connect over USB in Windows | Desk use, charging and initial debugging |
| Bluetooth | Pair in system settings, choose Bluetooth in Code Link, scan or paste the invitation, allow Nearby devices and choose the PC | Nearby access without prior USB pairing or PAN |
| LAN | Enable LAN access in Windows and scan or enter connection details; TCP connectivity is required | A reachable local network |

USB and Bluetooth retain their local connection paths. Use internet mode when campus isolation or carrier NAT prevents direct local access. [Detailed installation steps](docs/INSTALL.en.md)

## Your terminal, within reach

Connect existing PowerShell, Command Prompt, Windows Terminal and Codex windows. Phones use separate list and detail screens; larger tablets show both panes. Names and A/B groups are saved on the mobile device.

| Control | Behavior |
| --- | --- |
| Swipe down / up | Read earlier / later output. Swipe: scroll PC also moves the computer's viewport |
| Swipe: browse locally | Read synchronized text without changing the PC; new output preserves your reading position |
| Tap terminal content | Send a short tap to the corresponding text cell in a terminal that supports mouse input |
| Back to latest | Return ordinary history to its bottom; activate a visible Back to bottom / Back to latest control |
| Full screen, top right | Expand the terminal in landscape. Use Input when needed; top-left Back or system Back restores the previous layout |
| Text and shortcuts | Send text, Enter, Esc, Tab, arrows or Ctrl+C |

PC scrolling and taps support a **single Windows Terminal pane** and require control permission. Full-screen programs must support mouse interaction and have a restored PC window. Swipes, long presses and local browsing do not send clicks. A changed text line rejects a stale tap and asks you to retry.

Mappings follow Windows Terminal's **active tab**. Use separate windows to monitor tasks independently. External capture synchronizes text and spacing rather than full colors or graphical cursor effects, with approximately 120,000 characters of cache. Duplicate titles, higher-privilege targets or unsupported console interfaces can prevent input; the app reports the reason.

<details>
<summary><strong>Managed terminals and structured Codex sessions</strong></summary>

Run these from the extracted application directory:

```powershell
.\clink.exe terminal --cwd D:\project --name Project --new-window
.\clink.exe codex --cwd D:\project --name Assistant --new-window
.\clink.exe scan
```

`terminal` uses a real PTY with up to 5,000 lines in its terminal view. `codex` connects to the local Codex app-server for structured approvals, questions and turn events. Existing Codex windows can also be mapped directly and operated with text and shortcuts.

</details>

## A/B completion reminders

| Group | When it alerts | How it clears |
| --- | --- | --- |
| A · Individual | Each task completes | Read that terminal's reminder |
| B · Batch, the default | Every participating task in the current round succeeds | Read all participating terminals, or acknowledge the batch |

Idle terminals that never started do not block a B round. Failure, interruption, disconnection and unknown status do not count as success. Approvals and questions alert immediately in either group. Resolving them on the PC clears their mobile attention reminders after synchronization; marking a reminder as read does not authorize execution.

![Real Android completion banners for A and B groups](docs/assets/notifications.png)

Completion detection depends on the source: finite managed commands use exit status, `clink codex` uses turn events, and existing windows use supported screen markers. Unrecognized output stays unknown; silence alone never means success.

## Background connection

Android's foreground service owns the connection and reminders. Allow notifications, banners and background operation to receive alerts in another app or with the screen off. Do Not Disturb and manufacturer battery restrictions still apply. PC tasks continue during a disconnection; reconnecting restores current state without replaying unconfirmed input.

A direct iroh connection runs **over the internet between your devices**, without requiring Bluetooth range or a shared Wi-Fi network. When direct access fails, a public relay forwards encrypted traffic while your PC manages the terminal and task. Public relays are free, shared and rate-limited, with no availability guarantee.

Physical checks cover a phone on cellular data and a tablet on a hotspot connecting to a PC on campus Wi-Fi: input, swipes, taps, landscape viewing and resolved reminders passed. The tablet also received a completion alert unplugged and with its screen off. Overnight, eight-hour remote reliability and cross-carrier battery tests remain on the [roadmap](docs/ROADMAP.md). [Remote results](research/remote-acceptance.json) · [Phone controls](research/phone-terminal-acceptance.json) · [Full screen and reminders](research/phone-ui-acceptance.json)

## Troubleshooting

| Question | Answer |
| --- | --- |
| Do I need to pay or rent a server? | This version uses your PC and free public relays. Both devices need internet access |
| Can the phone run only Code Link? | Yes. Internet mode requires no separate VPN, proxy or USB debugging |
| Can I connect while the PC sleeps? | No. Keep it awake, online and running Code Link |
| Terminal text is too small | Use Full screen for landscape viewing; returning preserves the session and draft |
| Swipes or taps do not affect the PC | Check control permission, PC scrolling mode and a single Windows Terminal pane; full-screen programs must support mouse input |
| USB cannot find the device | Unlock it, authorize USB debugging and check the data cable and port. [USB setup](docs/INSTALL.en.md#usb) |
| No completion alert | Check notifications, background access, A/B grouping and a supported completion signal |
| Does iPhone have the same remote mode? | Apple currently uses LAN; iOS requires personal signing and pauses in the background. [Apple guide](docs/APPLE.md) |

<a id="macos-and-ios"></a>
## Documentation and contributing

- [Install and upgrade](docs/INSTALL.en.md) · [Remote design](docs/REMOTE.md) · [macOS and iOS](docs/APPLE.md)
- [Build from source](docs/BUILDING.md) · [Implementation](docs/implementation.zh-CN.md) · [Code tour](if_want_learn.md)
- [Changelog](CHANGELOG.md) · [Development record](docs/development-status.md) · [Roadmap](docs/ROADMAP.md)
- [Report a bug](https://github.com/ZARADEE/code_LINK/issues/new/choose) · [Ask a question](https://github.com/ZARADEE/code_LINK/discussions) · [Contributing](CONTRIBUTING.md)

Device reports are welcome: include your model, transport and reproducible steps. Code is [MIT licensed](LICENSE); artwork and dependencies have their own [asset notes](assets/README.md) and [third-party notices](THIRD_PARTY_NOTICES.md).

*Screenshots come from real devices or identified simulators. Earlier tablet images show local workflows; 2.0 phone behavior is documented in the linked acceptance records. [Image sources](docs/assets/README.md).*
