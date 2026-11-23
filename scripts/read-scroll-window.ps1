param([string]$StateFile='.local/scroll-test-session.json')
$test=Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$window=[System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$test.handle)
$nodes=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
$result=@{}
foreach($node in $nodes){
 if($node.Current.ControlType -eq [System.Windows.Automation.ControlType]::ScrollBar){
  $range=$node.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current
  $result['value']=$range.Value;$result['minimum']=$range.Minimum;$result['maximum']=$range.Maximum;$result['smallChange']=$range.SmallChange
 }
 if($node.Current.ClassName -eq 'TermControl'){
  $text=$node.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
  $visible=($text.GetVisibleRanges() | ForEach-Object { $_.GetText(-1) }) -join ''
  $result['visibleFirstLine']=($visible -split "`n")[0].Trim();$result['visibleLastLine']=($visible.Trim() -split "`n")[-1].Trim()
 }
}
$result | ConvertTo-Json -Compress
