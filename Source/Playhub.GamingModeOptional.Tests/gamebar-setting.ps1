$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$source=[IO.File]::ReadAllText((Join-Path $repo 'Source\Playhub\Assets\GamingMode\xbox-gamebar.ps1'))
$t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$t,[ref]$e)
$checks=0
function Assert([bool]$ok,[string]$label){if(-not $ok){throw "FAIL $label"};$script:checks++;Write-Output "PASS $label"}
Assert ($e.Count -eq 0) 'Production syntax'
foreach($name in @('Initialize-XboxSessionNative','Set-GameBar','Send-GameBarSettingChange')){
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 . ([scriptblock]::Create($node.Extent.Text))
}
Initialize-XboxSessionNative
Add-Type -ReferencedAssemblies System,System.Core,System.Windows.Forms @'
using System;using System.Threading;using System.Windows.Forms;using System.Runtime.InteropServices;
public sealed class BarSettingWindow:NativeWindow,IDisposable {
 readonly Thread worker;readonly ManualResetEvent ready=new ManualResetEvent(false);uint thread;
 public int Count,Delay;public string Area;public long Parameter;
 public BarSettingWindow(){worker=new Thread(Run){IsBackground=true};worker.SetApartmentState(ApartmentState.STA);worker.Start();if(!ready.WaitOne(2000))throw new Exception("Fixture window startup failed");}
 void Run(){thread=GetCurrentThreadId();CreateHandle(new CreateParams{Parent=new IntPtr(-3),Caption="Playhub isolated setting receiver"});ready.Set();Application.Run();DestroyHandle();}
 protected override void WndProc(ref Message m){if(m.Msg==0x1a){if(Delay>0)Thread.Sleep(Delay);Area=Marshal.PtrToStringUni(m.LParam);Parameter=m.WParam.ToInt64();Count++;m.Result=IntPtr.Zero;return;}base.WndProc(ref m);}
 public void Dispose(){PostThreadMessage(thread,0x12,IntPtr.Zero,IntPtr.Zero);worker.Join(2000);ready.Dispose();}
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]static extern bool PostThreadMessage(uint id,uint message,IntPtr w,IntPtr l);
}
'@
$receiver=[BarSettingWindow]::new()
try {
 Assert ([Playhub.GameBar.SessionNative]::NotifySettingChange($receiver.Handle)) 'Actual native setting notification reaches only isolated fixture window'
 Assert ($receiver.Count -eq 1 -and $receiver.Area -eq 'GameBar' -and $receiver.Parameter -eq 0) 'Notification uses WM_SETTINGCHANGE, registry leaf and zero wParam'
 $receiver.Delay=150;$watch=[Diagnostics.Stopwatch]::StartNew()
 Assert (-not [Playhub.GameBar.SessionNative]::NotifySettingChange($receiver.Handle)) 'Actual unresponsive receiver produces a timeout result'
 Assert ($watch.ElapsedMilliseconds -lt 100) 'Each receiver is bounded by the ten millisecond deadline'
} finally {$receiver.Dispose()}

foreach($scenario in @('On','Off','WriteFailure','ReadFailure','Mismatch','NotificationFailure')){
 & {
  $gameBarKey='HKCU:\FixtureOnly';$gameBarValue='UseNexusForGameBarEnabled'
  $script:logs=@();$script:notifications=0;$script:writes=0;$script:state=0
  function Write-Log([string]$message){$script:logs+=$message}
  function Test-Path {return $true}
  function Set-ItemProperty([string]$LiteralPath,[string]$Name,[int]$Value,[string]$Type,[string]$ErrorAction){
   [void](Assert ($ErrorAction -eq 'Stop') 'Production write requires terminating errors')
   $script:writes++;if($scenario -eq 'WriteFailure'){throw 'fixture write failure'};$script:state=$Value
  }
  function Get-ItemProperty([string]$LiteralPath,[string]$Name,[string]$ErrorAction){
   [void](Assert ($ErrorAction -eq 'Stop') 'Production readback requires terminating errors')
   if($scenario -eq 'ReadFailure'){throw 'fixture read failure'}
   return @{UseNexusForGameBarEnabled=if($scenario -eq 'Mismatch'){0}else{$script:state}}
  }
  function Send-GameBarSettingChange {$script:notifications++;return $scenario -ne 'NotificationFailure'}
  $value=if($scenario -eq 'Off'){0}else{1};$result=Set-GameBar $value
  $succeeded=$scenario -in @('On','Off','NotificationFailure')
  Assert ($result -eq $succeeded) ("Actual setting handler $scenario returns verified registry state")
  Assert ($script:notifications -eq $(if($succeeded){1}else{0})) ("Actual setting handler $scenario notifies only after correct readback")
  if($succeeded){Assert ($script:logs[-1] -match "requested=$value observed=$value notification=") 'Transition log distinguishes readback from notification receipt'}
 }
}

$loop=$ast.Find({param($n)$n -is [Management.Automation.Language.WhileStatementAst] -and $n.Extent.Text.StartsWith('while ($true)')},$true).Extent.Text
& {
 $script:tick=0;$script:state=0;$script:writes=@();$currentlyOn=$false
 function Test-GamingModeOptOut{return $script:tick -ge 3}
 function Test-SteamControllerProfileActive{return $false}
 function Test-XboxSessionRunning{return $true}
 function Get-GameBarState{return $script:state}
 function Set-GameBar([int]$value){$script:writes+=$value;$script:state=$value;return $true}
 function Write-Log([string]$message){}
 function Start-Sleep([int]$Seconds){$script:tick++;if($script:tick -eq 2){$script:state=0}}
 . ([scriptblock]::Create($loop))
 Assert (($script:writes -join ',') -eq '1,1,0') 'Stable enabled state is not rewritten; externally lost enabled value is restored on next existing tick'
}
Write-Output "$checks Game Bar setting checks passed. Only an isolated message window and registry spies were used; no broadcast or installed registry write occurred."
