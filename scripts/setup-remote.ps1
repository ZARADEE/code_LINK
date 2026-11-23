$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'remote-env.ps1')
& python (Join-Path $PSScriptRoot 'setup-remote.py')
if($LASTEXITCODE -ne 0){throw 'Remote toolchain setup failed'}
