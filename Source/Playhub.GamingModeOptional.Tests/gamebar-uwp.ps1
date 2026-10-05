$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$source = [IO.File]::ReadAllText((Join-Path $repo 'Source\Playhub\Assets\GamingMode\xbox-gamebar.ps1'))
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
$checks=0
function Assert([bool]$ok,[string]$label){if(-not $ok){throw "FAIL $label"};$script:checks++;Write-Output "PASS $label"}
Assert ($errors.Count -eq 0) 'Production script syntax'
foreach($name in @('Initialize-XboxSessionNative','Get-XboxSessionPipes','Open-XboxSessionProcess','Read-XboxSessionIdentity','Clear-XboxSession','Test-XboxSessionRunning')) {
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 . ([scriptblock]::Create($node.Extent.Text))
}
Initialize-XboxSessionNative
Add-Type -ReferencedAssemblies System,System.Core @'
using System;using System.IO.Pipes;using System.Text;using System.Threading;
public sealed class GameBarFixturePipe:IDisposable {
 readonly NamedPipeServerStream pipe;readonly Thread thread;public string Name;
 public GameBarFixturePipe(string name,string payload,bool stall) {
  Name=name;pipe=new NamedPipeServerStream(name,PipeDirection.Out,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
  thread=new Thread(()=>{try{pipe.WaitForConnection();if(stall){Thread.Sleep(1000);return;}var bytes=Encoding.UTF8.GetBytes(payload+"\n");pipe.Write(bytes,0,bytes.Length);}catch{}}){IsBackground=true};thread.Start();
 }
 public void Dispose(){pipe.Dispose();thread.Join(1500);}
}
public sealed class GameBarFixtureLease:IDisposable {
 public uint Pid,Session;public long Birth;public string Image,Package;public bool Alive=true,Disposed;
 public void Dispose(){Disposed=true;}
}
'@
$actual=Open-XboxSessionProcess ([uint32]$PID)
try {
 Assert ($actual.Alive -and $actual.Pid -eq $PID -and $actual.Birth -gt 0) 'Native held process lease observes real fixture host'
 Assert ($actual.Session -eq [Playhub.GameBar.SessionNative]::CurrentSession) 'Native process belongs to current Windows session'
 Assert ([IO.Path]::IsPathRooted($actual.Image)) 'Native image is an absolute kernel-observed path'
 $name='Playhub.UwpIdentity.'+$PID+'.'+$actual.Birth.ToString('X16')
 $json='{"fixture":"identity"}'
 $server=[GameBarFixturePipe]::new($name,$json,$false)
 try {
  Assert (@(Get-XboxSessionPipes | Where-Object { [IO.Path]::GetFileName($_) -eq $name }).Count -eq 1) 'Actual pipe-prefix discovery finds isolated identity publication'
  Assert ((Read-XboxSessionIdentity $name ([uint32]$PID)).Trim() -eq $json) 'Actual pipe authenticates server PID and reads complete bounded payload'
 } finally { $server.Dispose() }
 $server=[GameBarFixturePipe]::new($name,$json,$false)
 try { Assert ($null -eq (Read-XboxSessionIdentity $name ([uint32]($PID+1)))) 'Actual pipe rejects a different server PID' } finally { $server.Dispose() }
 $server=[GameBarFixturePipe]::new($name,$json,$true)
 try {
  $watch=[Diagnostics.Stopwatch]::StartNew()
  Assert ($null -eq (Read-XboxSessionIdentity $name ([uint32]$PID))) 'Actual stalled pipe is unavailable'
  Assert ($watch.ElapsedMilliseconds -lt 700) 'Actual stalled pipe read is bounded to 150 ms plus setup'
 } finally { $server.Dispose() }
 $server=[GameBarFixturePipe]::new($name,('a'*4096),$false)
 try { Assert ($null -eq (Read-XboxSessionIdentity $name ([uint32]$PID))) 'Actual oversized pipe payload is rejected' } finally { $server.Dispose() }
} finally { $actual.Dispose() }
Assert (-not $actual.Alive) 'Disposed native lease no longer authorizes a session'

# Actual production validation and cache with isolated metadata providers.
# No live game or registry is used; ReadIdentity's kernel authentication was tested above.
& {
 $script:xboxSessionWindowsSession=7; $script:xboxSessionHelper='C:\Fixture\Playhub.GameSession.exe'
 $script:enumerations=0;$script:reads=0;$script:opens=0;$script:legacy=$false
 function Get-XboxSessionPipes { $script:enumerations++; return $script:pipes }
 function Read-XboxSessionIdentity([string]$PipeName,[uint32]$WrapperPid){$script:reads++;return $script:payload}
 function Open-XboxSessionProcess([uint32]$ProcessId){$script:opens++;if($ProcessId -eq 200){return $script:wrapper};if($ProcessId -eq 300){return $script:game};return $null}
 function Test-HelperProcessRunning([string]$Name){if($Name -ne 'UWPHook'){throw 'Unexpected global process enumeration'};return $script:legacy}
 function Reset-Fixture {
  Clear-XboxSession
  $script:wrapper=[GameBarFixtureLease]::new();$script:wrapper.Pid=200;$script:wrapper.Birth=100;$script:wrapper.Session=7;$script:wrapper.Image=$script:xboxSessionHelper
  $script:game=[GameBarFixtureLease]::new();$script:game.Pid=300;$script:game.Birth=150;$script:game.Session=7;$script:game.Image='C:\Fixture\XboxGame.exe';$script:game.Package='Fixture.Family_abc'
  $script:identity=@{WrapperPid=200;WrapperBirth=100;GamePid=300;GameBirth=150;Aumid='Fixture.Family_abc!App';PackageFamily='Fixture.Family_abc';Executable=$script:game.Image}
  $script:payload=$script:identity|ConvertTo-Json -Compress
  $script:pipes=@('\\.\pipe\Playhub.UwpIdentity.200.0000000000000064')
  $script:xboxSession=$null
 }
 Reset-Fixture
 Assert (Test-XboxSessionRunning) 'Normal UWP publication authorizes Game Bar'
 $enumerations=$script:enumerations;$reads=$script:reads;$opens=$script:opens
 foreach($i in 1..20){ Assert (Test-XboxSessionRunning) "Pinned session remains alive $i" }
 Assert ($script:enumerations -eq $enumerations -and $script:reads -eq $reads -and $script:opens -eq $opens) 'Active session needs no pipe rescan, RPC, JSON parse or process reopen'
 $heldWrapper=$script:wrapper;$heldGame=$script:game;$heldGame.Alive=$false;$script:pipes=@()
 Assert (-not (Test-XboxSessionRunning)) 'Game exit disarms without trusting surviving wrapper'
 Assert ($heldWrapper.Disposed -and $heldGame.Disposed -and $null -eq $script:xboxSession) 'Exited session disposes both held process handles'
 foreach($bad in @('LocalWrapper','WrapperBirth','WrapperSession','GameBirth','GameSession','GameExecutable','Package','Aumid','MissingPackage','DeadGame','DeadWrapper','MalformedJson')) {
  Reset-Fixture
  switch($bad) {
   LocalWrapper {$script:wrapper.Image='C:\Fixture\EpicWrapper.exe'}
   WrapperBirth {$script:wrapper.Birth=101}
   WrapperSession {$script:wrapper.Session=8}
   GameBirth {$script:identity.GameBirth=151}
   GameSession {$script:game.Session=8}
   GameExecutable {$script:identity.Executable='C:\Fixture\Other.exe'}
   Package {$script:identity.PackageFamily='Other.Family_xyz'}
   Aumid {$script:identity.Aumid='Other.Family_xyz!App'}
   MissingPackage {$script:game.Package=''}
   DeadGame {$script:game.Alive=$false}
   DeadWrapper {$script:wrapper.Alive=$false}
  }
  $script:payload=$script:identity|ConvertTo-Json -Compress
  if($bad -eq 'MalformedJson'){$script:payload='invalid-json'}
  Assert (-not (Test-XboxSessionRunning)) ("Reject $bad identity")
  Assert ($script:wrapper.Disposed -and ($script:game.Disposed -or $bad -in @('LocalWrapper','WrapperBirth','WrapperSession','DeadWrapper','MalformedJson'))) ("Dispose rejected $bad leases")
 }
 Reset-Fixture;$script:pipes=@();Assert (-not (Test-XboxSessionRunning)) 'Local, Epic and GOG wrappers without a UWP publication cannot arm Game Bar'
 $script:legacy=$true;Assert (Test-XboxSessionRunning) 'Legacy UWPHook shortcuts remain compatible';$script:legacy=$false
 Clear-XboxSession
}

$loop=$ast.Find({param($n)$n -is [Management.Automation.Language.WhileStatementAst] -and $n.Extent.Text.StartsWith('while ($true)')},$true).Extent.Text
foreach($scenario in @('StartExit','NoGame','WriteRetry','ControllerOverride')) {
 & {
  $script:tick=0;$script:writes=@();$script:attempts=0;$script:detections=0;$currentlyOn=$scenario -eq 'ControllerOverride'
  function Test-GamingModeOptOut {return $script:tick -ge 2}
  function Test-SteamControllerProfileActive {return $scenario -eq 'ControllerOverride'}
  function Test-XboxSessionRunning {$script:detections++;return $scenario -eq 'WriteRetry' -or ($scenario -eq 'StartExit' -and $script:tick -eq 0)}
  function Get-GameBarState {return 0}
  function Set-GameBar([int]$Value){$script:writes+=$Value;$script:attempts++;return -not ($scenario -eq 'WriteRetry' -and $script:attempts -eq 1)}
  function Write-Log([string]$Message){}
  function Start-Sleep([int]$Seconds){Assert ($Seconds -eq 1) 'Production one-second cadence retained';$script:tick++}
  . ([scriptblock]::Create($loop))
  $expected=switch($scenario){StartExit{'1,0,0'} NoGame{'0'} WriteRetry{'1,1,0'} ControllerOverride{'0,0'}}
  Assert (($script:writes -join ',') -eq $expected) ("Actual loop $scenario transitions")
  if($scenario -eq 'ControllerOverride'){Assert ($script:detections -eq 0) 'Existing SteamController sidecar exclusion bypasses detection'}
 }
}
# Execute actual entrypoint finally, replacing only mutex and registry with spies.
$outer=$ast.Find({param($n)$n -is [Management.Automation.Language.TryStatementAst] -and $n.Finally.Extent.Text.Contains('$helperMutex.ReleaseMutex()')},$false)
foreach($failure in @($false,$true)) {
 & {
  $script:off=0;$script:released=0;$script:disposed=0;$script:cleared=0
  $helperMutex=New-Object PSObject
  $helperMutex|Add-Member ScriptMethod ReleaseMutex {$script:released++}
  $helperMutex|Add-Member ScriptMethod Dispose {$script:disposed++}
  function Set-GameBar([int]$Value){if($Value -eq 0){$script:off++};return $true}
  function Clear-XboxSession {$script:cleared++}
  try {if($failure){throw 'isolated crash'}} catch {} finally { . ([scriptblock]::Create($outer.Finally.Extent.Text.TrimStart('{').TrimEnd('}'))) }
  Assert ($script:off -eq 1 -and $script:cleared -eq 1 -and $script:released -eq 1 -and $script:disposed -eq 1) ("Actual finally restores OFF and disposes on crash=$failure")
 }
}
Write-Output "$checks Game Bar UWP checks passed. No installed registry, game or process was changed."
