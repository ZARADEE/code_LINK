param([Parameter(Mandatory=$true)][int]$AppProcessId,[string]$ExpectedTitle='撰写 Codex 终端状态提醒插件技术方案')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$process=Get-Process -Id $AppProcessId
$window=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
function Element([string]$id) {
    $condition=New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty),$id
    $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
$status=Element 'HostStatus'
$bounds=$window.Current.BoundingRectangle
$statusBounds=$status.Current.BoundingRectangle
if($statusBounds.Top -lt $bounds.Top+$bounds.Height*0.9 -or $statusBounds.Right -lt $bounds.Left+$bounds.Width*0.9) { throw 'Connection status is not at the bottom right' }
$list=Element 'ExternalProcessList'
$scan=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty),'扫描'))
$scan.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 500
$items=$list.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
$target=$items | Where-Object { $_.Current.Name.Contains($ExpectedTitle) } | Select-Object -First 1
if($null -eq $target) { throw 'The current Codex window is absent from the desktop list' }
$target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1500
if(-not (Element 'MapMobileButton').Current.IsEnabled) { throw 'The paired device cannot map an existing terminal' }
if($null -ne (Element 'SessionList')) { throw 'The redundant managed-terminal list is still present' }
$output=Element 'Output'
$content=$output.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
if($content.Length -lt 30 -or $content.StartsWith('正在读取')) { throw 'The real terminal content is absent' }
Start-Sleep -Seconds 6
if(-not (Element 'SessionTitle').Current.Name.Contains($ExpectedTitle)) { throw 'Automatic scan lost the selected window' }
$all=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
foreach($entry in $all) {
    if($entry.Current.Name -in @('创建会话','映射并交出控制','仅映射观察','收回控制','撤销设备','发送')) { throw ('Obsolete desktop control: '+$entry.Current.Name) }
}
# Restore through the application's activation event, never Win32 ShowWindow on a
# WPF window whose managed Visibility is Hidden (that only reveals a black HWND).
$window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Start-Sleep -Milliseconds 250
Start-Process -FilePath $process.Path -WindowStyle Hidden | Out-Null
Start-Sleep -Seconds 2
$process.Refresh()
$window=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if($null -eq (Element 'ExternalProcessList') -or $null -eq (Element 'Output')) { throw 'Panel content did not return after closing to tray' }
$instances=@(Get-Process CodeLink | Where-Object Path -eq $process.Path)
if($instances.Count -ne 1){throw 'Reopening the app created duplicate panels'}
$report=[pscustomobject]@{passed=$true;currentCodexVisible=$true;statusBottomRight=$true;automaticScanRetainsSelection=$true;externalMappingEnabled=$true;unifiedList=$true;realTerminalText=$true;trayRestoreContentVisible=$true;singleDesktopInstance=$true;obsoleteControlsAbsent=$true;statusText=$status.Current.Name}
$report | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $PSScriptRoot '../research/desktop-ui-acceptance.json')
$report
