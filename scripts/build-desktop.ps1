$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = if ($env:CODELINK_BUILD_OUTPUT) { Join-Path $root $env:CODELINK_BUILD_OUTPUT } else { Join-Path $root 'dist/desktop' }
$adbSource = Join-Path $root '.tools/platform-tools'

foreach ($project in @(
    'desktop/CodeLink.Desktop/CodeLink.Desktop.csproj',
    'desktop/CodeLink.Host/CodeLink.Host.csproj',
    'desktop/CodeLink.Cli/CodeLink.Cli.csproj',
    'desktop/CodeLink.ConsoleBridge/CodeLink.ConsoleBridge.csproj'
)) {
    & dotnet build (Join-Path $root $project) --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $output 'runtime.json'))) {
    [IO.File]::WriteAllText((Join-Path $output 'runtime.json'),'{"instance":"main","dataDirectory":"state"}',[Text.Encoding]::UTF8)
}
Copy-Item -Path (Join-Path $root 'desktop/CodeLink.Desktop/bin/Release/net10.0-windows/*') -Destination $output -Recurse -Force
Copy-Item -Path (Join-Path $root 'desktop/CodeLink.Host/bin/Release/net10.0/*') -Destination $output -Recurse -Force
Copy-Item -Path (Join-Path $root 'desktop/CodeLink.Cli/bin/Release/net10.0/*') -Destination $output -Recurse -Force
Copy-Item -Path (Join-Path $root 'desktop/CodeLink.ConsoleBridge/bin/Release/net10.0/*') -Destination $output -Recurse -Force

$tools = Join-Path $output 'tools'
New-Item -ItemType Directory -Force -Path $tools | Out-Null
Copy-Item -LiteralPath (Join-Path $adbSource 'adb.exe'), (Join-Path $adbSource 'AdbWinApi.dll'), (Join-Path $adbSource 'AdbWinUsbApi.dll') -Destination $tools -Force
Copy-Item -LiteralPath (Join-Path $adbSource 'NOTICE.txt') -Destination (Join-Path $tools 'ADB-NOTICE.txt') -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $output -Recurse -Force
Write-Output "Desktop app: $(Join-Path $output 'CodeLink.exe')"
Write-Output "Command line: $(Join-Path $output 'clink.exe')"
