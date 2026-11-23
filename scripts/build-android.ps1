param([switch]$Install,[ValidateSet('zh-CN','en')][string]$Language='zh-CN',[switch]$SkipNativeBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if(-not $SkipNativeBuild){ & (Join-Path $PSScriptRoot 'build-remote.ps1') -Platform Android }
$native = Join-Path $root '.local/remote-native/android'
if(-not(Test-Path "$native/lib/arm64-v8a/libcodelink_remote.so")){throw 'Missing Android remote library'}
$buildTools = Join-Path $root '.tools/android-sdk/build-tools/android-15'
$platform = Join-Path $root '.tools/android-sdk/platforms/android-35'
$source = Join-Path $root 'android/app/src/main'
if ($Language -eq 'en') {
    & python (Join-Path $PSScriptRoot 'localize-sources.py') generate
    if ($LASTEXITCODE -ne 0) { throw 'English translation validation failed' }
    $source = Join-Path $root '.local/localized/en/android/app/src/main'
}
$generated = Join-Path $root ".local/android-gen-$Language"
$classes = Join-Path $root ".local/android-classes-$Language"
$dex = Join-Path $root ".local/android-dex-$Language"
$classJar = Join-Path $root ".local/CodeLink-classes-$Language.jar"
$keystore = Join-Path $root '.local/CodeLink.debug.keystore'
$unsigned = Join-Path $root ".local/CodeLink-unsigned-$Language.apk"
$aligned = Join-Path $root ".local/CodeLink-aligned-$Language.apk"
$output = Join-Path $root "dist/CodeLink-Android-$Language.apk"
$androidJar = Join-Path $platform 'android.jar'
$zxing = Join-Path $root '.tools/zxing-core.jar'
$remoteNotices = Join-Path $root 'licenses/remote-third-party.txt'
$aapt = Join-Path $buildTools 'aapt.exe'
$javac = (Get-Command javac).Source
$d8 = Join-Path $buildTools 'd8.bat'
$jar = (Get-Command jar).Source
$zipalign = Join-Path $buildTools 'zipalign.exe'
$apksigner = Join-Path $buildTools 'apksigner.bat'

foreach ($file in @($androidJar, $zxing, $aapt, $d8, $zipalign, $apksigner, $remoteNotices)) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing Android build input: $file" }
}
foreach ($directory in @($generated, $classes, $dex, (Split-Path -Parent $output))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

function Invoke-Native([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed ($LASTEXITCODE): $Executable" }
}

$manifest = Join-Path $source 'AndroidManifest.xml'
$resources = Join-Path $source 'res'
$assets = Join-Path $source 'assets'
Invoke-Native $aapt @('package', '-f', '-m', '-J', $generated, '-M', $manifest, '-S', $resources, '-A', $assets, '-I', $androidJar, '-F', $unsigned)
$noticeStage = Join-Path $root '.local/remote-notices-apk'
New-Item -ItemType Directory -Force "$noticeStage/assets/licenses" | Out-Null
Copy-Item -Force $remoteNotices "$noticeStage/assets/licenses/remote-third-party.txt"
Push-Location $noticeStage
try { Invoke-Native $aapt @('add', '-f', $unsigned, 'assets/licenses/remote-third-party.txt') }
finally { Pop-Location }
$javaArguments = @('-encoding', 'UTF-8', '-Xlint:-options', '--release', '8', '-classpath', "$androidJar;$zxing", '-d', $classes)
$javaArguments += Get-ChildItem (Join-Path $source 'java') -Recurse -Filter '*.java' | ForEach-Object FullName
$javaArguments += Join-Path $generated 'com/codelink/app/R.java'
Invoke-Native $javac $javaArguments
Invoke-Native $jar @('--create', '--file', $classJar, '-C', $classes, '.')
Invoke-Native $d8 @('--lib', $androidJar, '--min-api', '28', '--output', $dex, $classJar, $zxing)
Push-Location $dex
try { Invoke-Native $aapt @('add', '-f', $unsigned, 'classes.dex') }
finally { Pop-Location }
Push-Location $native
try { Invoke-Native $aapt @('add', '-f', $unsigned, 'lib/arm64-v8a/libcodelink_remote.so') }
finally { Pop-Location }
Invoke-Native $zipalign @('-f', '-P', '16', '4', $unsigned, $aligned)

if (-not (Test-Path -LiteralPath $keystore)) {
    $keytool = (Get-Command keytool).Source
    Invoke-Native $keytool @('-genkeypair', '-keystore', $keystore, '-storepass', 'codelinkdebug', '-keypass', 'codelinkdebug', '-alias', 'codelink', '-keyalg', 'RSA', '-keysize', '2048', '-validity', '10000', '-dname', 'CN=Code Link Debug')
}
Invoke-Native $apksigner @('sign', '--ks', $keystore, '--ks-key-alias', 'codelink', '--ks-pass', 'pass:codelinkdebug', '--key-pass', 'pass:codelinkdebug', '--out', $output, $aligned)
Invoke-Native $apksigner @('verify', '--verbose', '--print-certs', $output)

if ($Install) {
    $adb = Join-Path $root '.tools/platform-tools/adb.exe'
    $port = $null
    foreach($candidate in @('5038','5037')) {
        $devices = & $adb -P $candidate devices -l
        if ($LASTEXITCODE -eq 0 -and ($devices | Where-Object { $_ -match '^\S+\s+device(?:\s|$)' })) { $port=$candidate;break }
    }
    if ($null -eq $port) { throw 'No authorized Android device on ADB port 5038 or 5037' }
    Invoke-Native $adb @('-P', $port, '-d', 'install', '-r', $output)
}
Write-Output "APK: $output"
