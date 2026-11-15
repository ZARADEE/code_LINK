param([Parameter(Mandatory=$true)][int]$DesktopPid,[ValidateSet('zh-CN','en')][string]$Language='zh-CN')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @'
using System;using System.Runtime.InteropServices;
public class WizardWindow {
 [DllImport("user32.dll")]public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int command);
}
'@
$process=Get-Process -Id $DesktopPid
$window=([System.Windows.Automation.AutomationElement]::RootElement).FindFirst(
 [System.Windows.Automation.TreeScope]::Children,
 (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty),$DesktopPid))
if(!$window){throw 'Desktop main window is absent'}
[WizardWindow]::ShowWindow([IntPtr]$window.Current.NativeWindowHandle,9) | Out-Null
function Named($scope,$name) {
 $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty),$name))
}
$pair=if($Language -eq 'en'){'Pair device'}else{'连接设备'}
$button=Named $window $pair
if(!$button){throw 'Pair device button is absent'}
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 800
$title=if($Language -eq 'en'){'First connection and device pairing'}else{'首次连接与设备配对'}
$dialog=Named ([System.Windows.Automation.AutomationElement]::RootElement) $title
if(!$dialog){throw 'Connection wizard is absent'}
$usb=if($Language -eq 'en'){'Connect over USB'}else{'USB 一键连接'}
$attempts=@()
try {
 foreach($round in 1..2) {
  (Named $dialog $usb).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $confirmed=$false
  for($i=0;$i -lt 50;$i++) {
   Start-Sleep -Milliseconds 500
   $texts=@($dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {$_.Current.Name})
   $confirmed=@($texts | Where-Object {$_ -like 'USB 已连接 · *' -or $_ -like 'USB connected · *'}).Count -gt 0
   if($confirmed -and (Named $dialog $usb).Current.IsEnabled){break}
  }
  if(!$confirmed){throw 'Android did not acknowledge this pairing attempt'}
  $attempts+=@{attempt=$round;authenticatedAndroidConnection=$true;retryAvailable=$true}
 }
 $out=Join-Path $root '.local/onboarding-review'
 New-Item -ItemType Directory -Force $out | Out-Null
 $bounds=$dialog.Current.BoundingRectangle
 $bitmap=New-Object System.Drawing.Bitmap ([int]$bounds.Width),([int]$bounds.Height)
 $graphics=[System.Drawing.Graphics]::FromImage($bitmap);$dc=$graphics.GetHdc()
 try{[WizardWindow]::PrintWindow([IntPtr]$dialog.Current.NativeWindowHandle,$dc,2) | Out-Null}finally{$graphics.ReleaseHdc($dc)}
 $bitmap.Save((Join-Path $out "connection-$Language.png"),[System.Drawing.Imaging.ImageFormat]::Png)
 $graphics.Dispose();$bitmap.Dispose()
 @{language=$Language;attempts=$attempts} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out "connection-$Language.json") -Encoding UTF8
 Write-Output "PASS: $Language connection wizard confirmed two physical Android connections"
} finally {
 $dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}
