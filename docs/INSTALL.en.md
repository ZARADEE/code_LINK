# Install Code Link in English

Download from [Code Link Releases](https://github.com/ZARADEE/code_LINK/releases/latest). Choose files ending in `-en`; `-zh-CN` means Simplified Chinese.

## Windows and Android

1. Download `CodeLink-Windows-v1.4.1-en.zip` and `CodeLink-Android-v1.4.1-en.apk`.
2. The Windows runtime is included. Extract the entire ZIP and open `CodeLink.exe`.
3. Install the APK on Android 9 or newer and allow notifications. Phone windows use a list/detail flow with a Back to list button. Tablets use two panes when at least 600dp of width and 480dp of height are available; split-screen adapts to the remaining window.
4. Enable USB debugging on Android, connect a data cable, unlock the device and authorize your computer.
5. In the Windows panel, choose **Pair device → Connect over USB**. Select a terminal on Android, or select a terminal and device in Windows and choose **Share with device**.
6. Send a short command and check the output. Use **More → Background & alerts** on Android to allow completion banners and background activity. Keep the Windows tray application running.

Group A alerts after each task completes. Group B alerts when all tasks in the current batch finish successfully. Approvals, failures and interruptions are handled separately. An idle terminal is not automatically treated as a finished task.

## Bluetooth

1. Pair Windows and Android in their system Bluetooth settings and keep both Code Link apps open.
2. If already paired in Code Link, choose **More → Bluetooth** on Android and select the PC. Existing USB app pairing can be reused.
3. For first-time app pairing, choose **Pair device → Bluetooth** in Windows to expand the existing pairing section. On Android choose **Connect → Bluetooth**, scan or paste the invitation, allow **Nearby devices**, and select the paired PC.
4. Wait for **Bluetooth connected**, select a test terminal, send `echo hello` and check its output.

Bluetooth requires no prior USB connection, USB debugging, Wi-Fi or PAN. The device picker links to system Bluetooth settings; return after pairing to refresh. Permission denial offers app settings, and an expired invitation can be refreshed in the Windows pairing window.

## Switch languages or upgrade

The Chinese and English APKs use the same app identity and release signing certificate. Install the other language over the existing app to keep pairing and group settings; do not uninstall first. They are language variants of one application and cannot be installed side by side.

For Windows, finish or record active work, close Code Link and its host, then replace the application files while preserving your `state` directory. Use one language consistently for the desktop panel and bundled host. The ZIP contains a `LANGUAGE.txt` marker.

The language applies to Code Link's interface and messages. Terminal output, filenames, session names, external tool prompts and operating-system dialogs retain their original language.

## Verify the download

Compare these results with `SHA256SUMS.txt` from the same release:

```powershell
Get-FileHash .\CodeLink-Windows-v1.4.1-en.zip -Algorithm SHA256
Get-FileHash .\CodeLink-Android-v1.4.1-en.apk -Algorithm SHA256
```

The APK uses a development signing certificate. Windows executables do not have a commercial code-signing certificate. Source builds and CI test APKs may use a different Android signing key, so they cannot replace the published APK without a matching key.

## macOS and iOS

Apple packages also end in `-en`. Mac apps include the runtime and are available for Apple silicon and Intel. They are ad-hoc signed and not notarized. iOS provides a simulator app, an unsigned device archive and a source project for personal signing; there is no directly installable signed IPA or TestFlight release. Follow the [Apple guide](https://github.com/ZARADEE/code_LINK/blob/main/docs/APPLE.md).

For Bluetooth, pairing, notifications and troubleshooting, read the [English usage guide](https://github.com/ZARADEE/code_LINK/blob/main/README.md).
