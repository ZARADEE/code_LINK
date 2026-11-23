param([Parameter(Mandatory=$true)][int]$DesktopPid)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$automation=[System.Windows.Automation.AutomationElement]
$scope=[System.Windows.Automation.TreeScope]::Descendants
function ByName($parent,$name){$parent.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new($automation::NameProperty,$name))}
function ById($parent,$id){$parent.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new($automation::AutomationIdProperty,$id))}
function Invoke($element){if(-not $element){throw 'Missing UI control'};$element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()}
function Await($check){for($i=0;$i -lt 100;$i++){if(& $check){return};Start-Sleep -Milliseconds 500};throw 'Remote UI state timed out'}
$window=$automation::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new($automation::ProcessIdProperty,$DesktopPid))
if(-not $window){throw 'Desktop window not found'}
Invoke (ByName $window '连接设备')
$script:dialog=$null
Await { $script:dialog=ByName $automation::RootElement '首次连接与设备配对'; $null -ne $script:dialog }
function Invitation {
    $box=ById $script:dialog 'PairRemoteInvitation'
    if(-not $box){return $null}
    $value=$box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if(-not $value.StartsWith('codelink:')){return $null}
    $encoded=$value.Substring(9).Replace('-','+').Replace('_','/');$encoded=$encoded.PadRight($encoded.Length+(4-$encoded.Length%4)%4,'=')
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded)) | ConvertFrom-Json
}
try {
    Await { $null -ne (Invitation) }
    $before=Invitation
    if($before.transport -ne 'Remote' -or $before.remoteProtocol -ne 1 -or $before.code -notmatch '^[a-f0-9]{64}$' -or $before.endpoint -notmatch '^[a-f0-9]{64}$'){throw 'Invalid remote invitation'}
    Invoke (ById $script:dialog 'PairRemoteRefresh')
    Await { $current=Invitation; $null -ne $current -and $current.code -ne $before.code }
    Invoke (ById $script:dialog 'PairRemoteToggle')
    Await { (ById $script:dialog 'PairRemoteStatus').Current.Name -eq '远程连接已关闭。' }
    Invoke (ById $script:dialog 'PairRemoteToggle')
    Await { $current=Invitation; $null -ne $current -and (ById $script:dialog 'PairRemoteStatus').Current.Name.StartsWith('远程入口已就绪') }
    $after=Invitation
    if($after.endpoint -ne $before.endpoint){throw 'Endpoint identity changed after toggling remote access'}
    Write-Output 'PASS: remote invitation, refresh, disable/enable, persistent endpoint identity'
}finally{$script:dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}
