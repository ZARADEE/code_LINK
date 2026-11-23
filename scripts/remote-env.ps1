$ErrorActionPreference = 'Stop'
$remoteRoot = Split-Path -Parent $PSScriptRoot
# A shared compiler is read-only; downloads, temporary files and build output
# always belong to this workspace. CODELINK_TOOLCHAIN_ROOT can override discovery.
$remoteToolchain = $env:CODELINK_TOOLCHAIN_ROOT
if (-not $remoteToolchain) {
    $remoteCandidate = Join-Path (Split-Path -Parent $remoteRoot) 'Campus-Recruitment-Progress-Record/.toolchain'
    if (Test-Path "$remoteCandidate/rustup/toolchains/stable-x86_64-pc-windows-msvc/bin/rustc.exe") { $remoteToolchain = $remoteCandidate }
}
$env:CARGO_HOME = Join-Path $remoteRoot '.tools/cargo'
$env:CARGO_TARGET_DIR = Join-Path $remoteRoot '.local/remote-target'
$env:TEMP = Join-Path $remoteRoot '.local/remote-tmp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:CARGO_HOME,$env:CARGO_TARGET_DIR,$env:TEMP | Out-Null
if ($remoteToolchain) {
    $remoteRust = Join-Path $remoteToolchain 'rustup/toolchains/stable-x86_64-pc-windows-msvc/bin'
    $env:RUSTC = Join-Path $remoteRust 'rustc.exe'
    $env:RUSTDOC = Join-Path $remoteRust 'rustdoc.exe'
    $env:PATH = "$remoteRust;$env:PATH"
    $remoteVc = Get-ChildItem "$remoteToolchain/msvc/VC/Tools/MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $remoteSdk = Join-Path $remoteToolchain 'msvc/Windows Kits/10'
    $remoteSdkVersion = (Get-ChildItem "$remoteSdk/Include" -Directory | Sort-Object Name -Descending | Select-Object -First 1).Name
    $env:VCToolsInstallDir = "$($remoteVc.FullName)\"
    $env:VCToolsVersion = $remoteVc.Name
    $env:WindowsSdkDir = "$remoteSdk\"
    $env:WindowsSDKVersion = "$remoteSdkVersion\"
    $env:VSCMD_ARG_HOST_ARCH = 'x64'
    $env:VSCMD_ARG_TGT_ARCH = 'x64'
    $env:INCLUDE = "$($remoteVc.FullName)/include;$remoteSdk/Include/$remoteSdkVersion/ucrt;$remoteSdk/Include/$remoteSdkVersion/shared;$remoteSdk/Include/$remoteSdkVersion/um"
    $env:LIB = "$($remoteVc.FullName)/lib/x64;$remoteSdk/Lib/$remoteSdkVersion/ucrt/x64;$remoteSdk/Lib/$remoteSdkVersion/um/x64"
    $env:PATH = "$($remoteVc.FullName)/bin/Hostx64/x64;$remoteSdk/bin/$remoteSdkVersion/x64;$env:PATH"
}
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) { throw 'Rust 1.91+ with a Windows MSVC toolchain is required.' }
