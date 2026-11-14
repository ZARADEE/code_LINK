param([Parameter(Mandatory=$true)][int]$AppProcessId,[Parameter(Mandatory=$true)][string]$Title)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$window=[System.Windows.Automation.AutomationElement]::FromHandle((Get-Process -Id $AppProcessId).MainWindowHandle)
function Find([string]$id){$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty),$id))}
$list=Find 'ExternalProcessList'
$target=$list.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object {$_.Current.Name.Contains($Title)} | Select-Object -First 1
if(!$target){throw 'Terminal window is absent from the list'}
$target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1200
$button=Find 'MapMobileButton'
if(!$button.Current.IsEnabled){throw ('Mapping is disabled: '+$button.Current.Name)}
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
