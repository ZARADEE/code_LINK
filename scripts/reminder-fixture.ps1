param([Parameter(Mandatory=$true)][string]$GatePath,[int]$Result=0)
Write-Output 'REMINDER_TASK_RUNNING'
while(-not (Test-Path -LiteralPath $GatePath)){Start-Sleep -Milliseconds 150}
Write-Output 'REMINDER_TASK_FINISHED'
exit $Result
