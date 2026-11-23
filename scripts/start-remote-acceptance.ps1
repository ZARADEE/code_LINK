param([switch]$Restart,[switch]$Direct)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$remoteData=Join-Path $root '.local/remote-acceptance'
$remoteExe=Join-Path $root 'dist/desktop-remote/CodeLink.Host.exe'
New-Item -ItemType Directory -Force $remoteData | Out-Null
if(-not(Test-Path "$remoteData/host.json")){[IO.File]::WriteAllText("$remoteData/host.json",'{"Port":47639,"BluetoothEnabled":false}')}
if(Test-Path "$remoteData/host.pid"){
    $remotePrior=Get-Process -Id ([int](Get-Content "$remoteData/host.pid")) -ErrorAction SilentlyContinue
    if($remotePrior){
        if($remotePrior.Path -ne $remoteExe){throw 'The saved PID belongs to a different executable'}
        if(-not $Restart){Write-Output 'Test host is already running';exit 0}
        Stop-Process -Id $remotePrior.Id
        $remotePrior.WaitForExit(5000) | Out-Null
    }
}
$env:CODELINK_DATA_DIR=$remoteData
$env:CODELINK_INSTANCE='remote-acceptance'
$env:CODELINK_REMOTE_RELAY_ONLY=if($Direct){'0'}else{'1'}
$remoteProcess=Start-Process -FilePath $remoteExe -WindowStyle Hidden -PassThru
$remoteProcess.Id | Set-Content "$remoteData/host.pid"
Write-Output "Isolated test host PID: $($remoteProcess.Id)"
