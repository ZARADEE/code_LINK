# Install and upgrade Code Link 2.0

Windows hosts the task; your Android phone or tablet views and controls it. The Windows package targets x64; the Android APK requires ARM64 and Android 9 or newer. macOS / iOS use LAN; see the [Apple guide](APPLE.md).

## Download and run

Get the Windows ZIP, Android APK and `SHA256SUMS.txt` from the [v2.0.0 release](https://github.com/ZARADEE/code_LINK/releases/tag/v2.0.0).

1. Extract all of `CodeLink-Windows-v2.0.0-en.zip` and run `CodeLink.exe`. The .NET 10 Desktop Runtime is included.
2. Install `CodeLink-Android-v2.0.0-en.apk`, allowing the installation source and notifications when prompted.
3. Connect using one of the methods below. Select a terminal on Android, or select a terminal and online device in Windows and choose Share with device.
4. Send `echo hello` to a test terminal. For alerts, open More → Background & alerts and allow banners and background operation.

The `en` suffix identifies English; `zh-CN` identifies Simplified Chinese. [中文说明](INSTALL.md).

## Internet remote access

1. In Windows, open Pair device, enable remote access and wait for the endpoint to be ready.
2. On Android, choose Connect → Scan QR and scan the **remote invitation**, or paste its pairing details.
3. The first invitation lasts 10 minutes; refresh it if expired. Paired devices save credentials for future connections.
4. Use cellular data, a hotspot or another Wi-Fi network. Keep the PC online, awake and running Code Link.

No USB debugging, mobile VPN, port forwarding or rented server is required. iroh tries direct access and falls back to free public relays. If pairing fails, check that you used the remote invitation, it has not expired and the PC is online. [Design and tested scope](REMOTE.md).

## USB

1. Enable Android Developer options and USB debugging. Attach a data cable, unlock the device and authorize the computer.
2. In Windows, choose Pair device → Connect over USB and wait for application authentication to finish.
3. If the device is absent, check the data cable and port. Run `tools\adb.exe -P 5037 devices -l` from the app directory; `unauthorized` means Android needs your confirmation. Check port 5038 if necessary.

Unplugging disconnects USB mode. Pair through the remote entry for internet use.

## Bluetooth

1. Pair the devices in system Bluetooth settings and open both Code Link apps.
2. For initial app trust, choose Pair device → Bluetooth on Windows, then Connect → Bluetooth on Android. Scan or paste the invitation, allow Nearby devices and choose the PC.
3. Paired devices can reconnect through More → Bluetooth; existing USB application trust can be reused.

Bluetooth needs no earlier USB connection, USB debugging, Wi-Fi or PAN. Permission denial links to app settings. If the PC is not paired, use system settings first and return to refresh.

## Reading and alerts

Phones use list/detail navigation; larger tablets can show both panes. Swipe down for earlier output and up for later output. PC scrolling requires control permission; local browsing reads cached output.

Full screen opens a landscape terminal. Top-left Back or system Back restores the layout and draft. Back to latest can activate the terminal's Back to bottom control. Handling an authorization on the PC clears its mobile attention reminder when resumed state arrives; completion alerts retain their A/B acknowledgement rules.

## Upgrade from 1.x or switch languages

Official English and Chinese APKs share the existing signing certificate and application identity. Install over the old app to keep pairing and groups; do not uninstall first. Android 2.0 uses `versionCode` 8. Source and CI APKs may have a different key and cannot replace an official APK unless signatures match.

On Windows, save task state and exit Code Link and its host before replacing application files. Keep your `state` directory. Existing terminal windows may need remapping after restart. Remote pairing uses a dedicated invitation; existing USB/Bluetooth trust does not automatically enable the remote endpoint.

## Verify downloads

```powershell
Get-FileHash .\CodeLink-Windows-v2.0.0-en.zip -Algorithm SHA256
Get-FileHash .\CodeLink-Android-v2.0.0-en.apk -Algorithm SHA256
```

Compare with `SHA256SUMS.txt` from the same release. Android retains its development signing certificate, whose fingerprint is listed on the release page. Windows executables are not commercially code-signed. Preserve private pairing data in `state` when upgrading; do not share it.
