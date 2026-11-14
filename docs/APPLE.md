# Code Link for macOS and iOS

The Apple clients use the same TLS protocol as Windows and Android. macOS hosts managed zsh terminals and local Codex CLI sessions. iPhone and iPad connect over your local network to a Mac or Windows host.

Requirements: macOS 14 or newer; iOS / iPadOS 17 or newer. macOS packages are provided for Apple silicon and Intel.

## Downloads and signing

The macOS application is ad-hoc signed, without an Apple Developer ID or notarization. Download it from this repository's release, unzip it, and move CodeLink.app to Applications. If macOS blocks it, use System Settings → Privacy & Security → Open Anyway after checking the release checksum. Do not disable Gatekeeper globally.

There is no signed iPhone IPA or TestFlight release. `CodeLink-iOS-Simulator` runs in Xcode's iOS Simulator on a Mac. `CodeLink-iOS-unsigned-archive` is an unsigned device archive for developers; it cannot be installed directly on an iPhone. `CodeLink-Apple-Source` contains an Xcode project for personal signing. Open `apple/CodeLink.xcodeproj`, select CodeLinkiOS, set **Build Settings → Code Signing Allowed** to **Yes**, enable automatic signing, select your own development team and bundle identifier, and run on a connected device. A free Apple Account can be used for personal device testing under Apple's provisioning limits. App Store / TestFlight distribution needs paid Apple Developer Program membership.

## First connection

1. Open the Mac app and create a terminal or Codex session. Codex requires a separately installed, authenticated Codex CLI.
2. Open Pair devices, enable local network connections and check the displayed Mac IP address.
3. Open the iOS app on the same network. Scan the pairing QR code or paste its invitation. Allow camera and local network access when prompted. Verify the certificate fingerprint against the computer.
4. Open a session from the phone's command list. You can also share a session with a paired device from the Mac session menu.
5. Use the terminal, command composer and approval cards. Assign sessions to group A for individual completion reminders, or group B for one reminder when a batch finishes. Approvals and failures alert separately.

macOS keeps its host and terminals alive while Code Link is open. Quitting the Mac app stops its host and managed terminals. Keep it open when using the phone. Pairing secrets are encrypted with a key stored in macOS Keychain; iOS stores its device credential in Keychain.

## Platform differences

| Capability | macOS / iOS support |
| --- | --- |
| Managed terminals, streaming output and input | Yes, macOS zsh PTY with iOS companion |
| Codex prompts, approvals, questions and interruption | Yes, using local Codex CLI on the host |
| A/B grouping, unread state and local notifications | Yes, while connected |
| Arbitrary external terminal window capture | Windows host only; macOS shares sessions created in Code Link |
| USB ADB / classic Bluetooth transport | Android + Windows only; Apple clients use LAN TLS |
| iOS background connection | Suspended in background; reconnects on return; intermediate events may be missed |
| Push notifications while iOS is suspended | Not included; no cloud push service |
| Developer ID notarization / TestFlight | Not available in this release |

## Build and verification

Install Xcode, its command-line tools, .NET 10 SDK and XcodeGen (`brew install xcodegen`) on a Mac. Run `bash scripts/build-apple.sh` from the repository root. Output is in `dist/apple`. The script builds both Mac architectures, an iOS simulator app, an unsigned iOS device archive and a source archive. It runs protocol / reminder checks and exercises the real macOS host.

The GitHub Apple workflow performs the same build on macOS. Build logs, checksums and screenshots are uploaded as workflow artifacts. Unsigned archives are clearly separated from runnable simulator packages.

Apple references: [distribution options](https://developer.apple.com/documentation/xcode/distributing-your-app-for-beta-testing-and-releases), [free account and program comparison](https://developer.apple.com/support/compare-memberships/), [opening apps safely](https://support.apple.com/en-us/102445).
