param([ValidateSet('Windows','Android','All')][string]$Platform='All', [switch]$Debug)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'remote-env.ps1')
$remoteProfile = if($Debug){'debug'}else{'release'}
$remoteCargoArgs = @('build','--manifest-path',(Join-Path $remoteRoot 'native/remote/Cargo.toml'),'--locked','-j','4')
if(-not $Debug){ $remoteCargoArgs += '--release' }
if($Platform -in @('Windows','All')) {
    # Keep the native DLL usable on PCs without a separate VC++ Redistributable.
    $env:CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_RUSTFLAGS = '-C target-feature=+crt-static'
    & cargo @remoteCargoArgs --target x86_64-pc-windows-msvc
    if($LASTEXITCODE -ne 0){throw 'Windows remote library build failed'}
    New-Item -ItemType Directory -Force (Join-Path $remoteRoot '.local/remote-native/windows') | Out-Null
    Copy-Item -Force "$env:CARGO_TARGET_DIR/x86_64-pc-windows-msvc/$remoteProfile/codelink_remote.dll" (Join-Path $remoteRoot '.local/remote-native/windows/')
}
if($Platform -in @('Android','All')) {
    $remoteNdk = if($env:CODELINK_ANDROID_NDK){$env:CODELINK_ANDROID_NDK}else{Join-Path $remoteRoot '.tools/android-ndk-r27c'}
    $remoteClang = Join-Path $remoteNdk 'toolchains/llvm/prebuilt/windows-x86_64/bin'
    $remoteSysroot = Join-Path $remoteRoot '.tools/android-rust-sysroot'
    if(-not(Test-Path "$remoteClang/aarch64-linux-android28-clang.cmd")){throw 'Missing Android NDK r27c (CODELINK_ANDROID_NDK)'}
    if(-not(Test-Path "$remoteSysroot/lib/rustlib/aarch64-linux-android/lib")){throw 'Missing Android Rust standard library; see docs/REMOTE.md'}
    $env:CC_aarch64_linux_android = "$remoteClang/aarch64-linux-android28-clang.cmd"
    $env:AR_aarch64_linux_android = "$remoteClang/llvm-ar.exe"
    $env:CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER = "$remoteClang/aarch64-linux-android28-clang.cmd"
    $env:CARGO_TARGET_AARCH64_LINUX_ANDROID_RUSTFLAGS = "--sysroot=$remoteSysroot -C link-arg=-Wl,-z,max-page-size=16384"
    & cargo @remoteCargoArgs --target aarch64-linux-android
    if($LASTEXITCODE -ne 0){throw 'Android remote library build failed'}
    $remoteAndroidOutput = Join-Path $remoteRoot '.local/remote-native/android/lib/arm64-v8a'
    New-Item -ItemType Directory -Force $remoteAndroidOutput | Out-Null
    Copy-Item -Force "$env:CARGO_TARGET_DIR/aarch64-linux-android/$remoteProfile/libcodelink_remote.so" $remoteAndroidOutput
    & "$remoteClang/llvm-strip.exe" "$remoteAndroidOutput/libcodelink_remote.so"
    if($LASTEXITCODE -ne 0){throw 'Android library stripping failed'}
}
Write-Output "Native transport built: $Platform ($remoteProfile)"
