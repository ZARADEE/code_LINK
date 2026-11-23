param([ValidateSet('zh-CN','en')][string]$Language='zh-CN',[switch]$SkipNativeBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if(-not $SkipNativeBuild){ & (Join-Path $PSScriptRoot 'build-remote.ps1') -Platform Windows }
$remoteLibrary = Join-Path $root '.local/remote-native/windows/codelink_remote.dll'
if(-not(Test-Path $remoteLibrary)){throw 'Missing Windows remote library'}
$projectRoot = $root
if ($Language -eq 'en') {
    & python (Join-Path $PSScriptRoot 'localize-sources.py') generate
    if ($LASTEXITCODE -ne 0) { throw 'English translation validation failed' }
    $projectRoot = Join-Path $root '.local/localized/en'
}
$defaultOutput = if ($Language -eq 'en') { 'dist/desktop-en' } else { 'dist/desktop' }
$output = if ($env:CODELINK_BUILD_OUTPUT) { Join-Path $root $env:CODELINK_BUILD_OUTPUT } else { Join-Path $root $defaultOutput }
$adbSource = Join-Path $root '.tools/platform-tools'
$stage = Join-Path $root ('.local/publish/' + $Language + '/' + [Guid]::NewGuid().ToString('N'))
$bundle = Join-Path $stage 'bundle'

New-Item -ItemType Directory -Force -Path $bundle | Out-Null
foreach ($project in @(
    'desktop/CodeLink.Host/CodeLink.Host.csproj',
    'desktop/CodeLink.Cli/CodeLink.Cli.csproj',
    'desktop/CodeLink.ConsoleBridge/CodeLink.ConsoleBridge.csproj',
    # Publish the desktop runtime last: its WindowsBase.dll supersedes the
    # compatibility facade supplied by the console runtime.
    'desktop/CodeLink.Desktop/CodeLink.Desktop.csproj'
)) {
    $publish = Join-Path $stage ([IO.Path]::GetFileNameWithoutExtension($project))
    & dotnet publish (Join-Path $projectRoot $project) --configuration Release --runtime win-x64 --self-contained true --output $publish -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
    Copy-Item -Path (Join-Path $publish '*') -Destination $bundle -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $output 'runtime.json'))) {
    [IO.File]::WriteAllText((Join-Path $output 'runtime.json'),'{"instance":"main","dataDirectory":"state"}',[Text.Encoding]::UTF8)
}

$tools = Join-Path $bundle 'tools'
Copy-Item -LiteralPath $remoteLibrary -Destination $bundle -Force
New-Item -ItemType Directory -Force -Path $tools | Out-Null
Copy-Item -LiteralPath (Join-Path $adbSource 'adb.exe'), (Join-Path $adbSource 'AdbWinApi.dll'), (Join-Path $adbSource 'AdbWinUsbApi.dll') -Destination $tools -Force
Copy-Item -LiteralPath (Join-Path $adbSource 'NOTICE.txt') -Destination (Join-Path $tools 'ADB-NOTICE.txt') -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $bundle -Force
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $bundle -Recurse -Force
$files = @(Get-ChildItem -LiteralPath $bundle -File -Recurse | ForEach-Object { $_.FullName.Substring($bundle.Length + 1).Replace('\', '/') } | Sort-Object)
Copy-Item -Path (Join-Path $bundle '*') -Destination $output -Recurse -Force
# Packaging only consumes files from this build; old runtime files and local state stay out.
[IO.File]::WriteAllText((Join-Path $output 'build-files.json'), (ConvertTo-Json -InputObject $files), [Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $output 'build-language.txt'),$Language,[Text.Encoding]::UTF8)
Write-Output "Desktop app: $(Join-Path $output 'CodeLink.exe')"
Write-Output "Command line: $(Join-Path $output 'clink.exe')"
