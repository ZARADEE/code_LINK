param([Parameter(Mandatory=$true)][int]$DesktopPid,[ValidateSet('zh-CN','en')][string]$Language='zh-CN')
# Use an isolated host with no paired devices. This checks the real WPF wizard,
# not the Bluetooth radio or Android permissions. Never print invitation secrets.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$scope=[System.Windows.Automation.TreeScope]::Descendants
function Named($parent,[string]$name) {
    $parent.FindFirst($scope,(New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty),$name))
}
function Identified($parent,[string]$id) {
    $parent.FindFirst($scope,(New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty),$id))
}
function InvokeButton($button) {
    if(!$button){throw 'Expected wizard button is absent'}
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function WaitFor([scriptblock]$condition,[string]$failure) {
    for($attempt=0;$attempt -lt 60;$attempt++) {
        if(& $condition){return}
        Start-Sleep -Milliseconds 100
    }
    throw $failure
}
$desktop=[System.Windows.Automation.AutomationElement]::RootElement
$window=$desktop.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty),$DesktopPid))
if(!$window){throw 'Desktop main window is absent'}
$pair=if($Language -eq 'en'){'Pair device'}else{'连接设备'}
$title=if($Language -eq 'en'){'First connection and device pairing'}else{'首次连接与设备配对'}
$dialog=$null
try {
    InvokeButton (Named $window $pair)
    $dialogCondition=New-Object System.Windows.Automation.AndCondition (
        (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty),$DesktopPid),
        (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty),$title))
    WaitFor { $script:dialog=$desktop.FindFirst($scope,$dialogCondition); $null -ne $script:dialog } 'Pairing window did not open'
    InvokeButton (Identified $dialog 'PairBluetooth')
    $script:invitationBox=$null
    WaitFor { $script:invitationBox=Identified $dialog 'PairInvitation'; $null -ne $script:invitationBox } 'Existing pairing section did not expand'
    function Invitation {
        $value=$script:invitationBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if(!$value.StartsWith('codelink:')){throw 'Invitation prefix is invalid'}
        $base64=$value.Substring(9).Replace('-','+').Replace('_','/')
        $base64=$base64.PadRight($base64.Length+(4-$base64.Length%4)%4,'=')
        $result=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($base64)) | ConvertFrom-Json
        if($result.pin -notmatch '^[a-fA-F0-9]{64}$' -or !$result.code -or $result.port -lt 1){throw 'Invitation fields are invalid'}
        return $result
    }
    WaitFor { (Invitation).transport -eq 'Bluetooth' } 'Bluetooth entry generated the wrong transport'
    $first=Invitation
    $status=Identified $dialog 'PairBluetoothStatus'
    if(!$status){throw 'Bluetooth status is absent'}
    $empty=if($Language -eq 'en'){'No devices connected over Bluetooth yet.'}else{'尚无通过蓝牙连接的设备。'}
    if(!$status.Current.Name.Contains($empty)){throw 'An isolated host must not claim a Bluetooth connection'}
    InvokeButton (Identified $dialog 'PairRefresh')
    WaitFor { (Invitation).code -ne $first.code } 'Refresh did not rotate the pairing code'
    $fresh=Invitation
    if($fresh.transport -ne 'Bluetooth' -or $fresh.pin -ne $first.pin){throw 'Refresh changed transport or host identity'}
    $combo=Identified $dialog 'PairTransport'
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $script:usb=$null
    WaitFor { $script:usb=Named $combo 'USB'; $null -ne $script:usb } 'USB option is absent'
    $script:usb.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    WaitFor { (Invitation).transport -eq 'USB' } 'Switching back to USB kept a Bluetooth invitation'
    if((Invitation).host -ne '127.0.0.1'){throw 'USB invitation lost the loopback address'}
    InvokeButton (Identified $dialog 'PairBluetooth')
    WaitFor { (Invitation).transport -eq 'Bluetooth' } 'Bluetooth entry did not restore its transport'
    Write-Output "PASS: $Language Bluetooth entry, invitation transport, honest offline status, code refresh and USB switching"
} finally {
    if($dialog){$dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}
}
