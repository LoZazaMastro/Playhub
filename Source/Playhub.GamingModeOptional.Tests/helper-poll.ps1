param([string]$BeforeDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$assets=Join-Path $repo 'Source\Playhub\Assets\GamingMode'
$before=if($BeforeDirectory){$BeforeDirectory}else{Join-Path $repo '.local\red-code\helper-poll-before-20261003'}
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('Playhub-helper-poll-'+[Guid]::NewGuid().ToString('N'))
$shell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$children=[Collections.Generic.List[Diagnostics.Process]]::new()
$checks=0
function Assert([bool]$ok,[string]$label){if(-not $ok){throw "FAIL $label"};$script:checks++;Write-Output "PASS $label"}
Add-Type -ReferencedAssemblies System,System.Core @'
using System;using System.Net;using System.Text;using System.Threading;using System.Collections.Concurrent;
public sealed class PollServer:IDisposable{
 readonly HttpListener listener=new HttpListener();readonly Thread worker;
 public string Mode="Gaming";public int Code=200;public int Delay=0;public bool Broken=false;public bool StallBody=false;public bool Legacy=false;
 public readonly ConcurrentQueue<string> Requests=new ConcurrentQueue<string>();public readonly ConcurrentDictionary<int,int> Ports=new ConcurrentDictionary<int,int>();
 public PollServer(int port){listener.Prefixes.Add("http://127.0.0.1:"+port+"/");listener.Start();worker=new Thread(Run){IsBackground=true};worker.Start();}
 void Run(){while(listener.IsListening){HttpListenerContext c;try{c=listener.GetContext();}catch{return;}
  ThreadPool.QueueUserWorkItem(_=>{try{Requests.Enqueue(c.Request.RawUrl);Ports.TryAdd(c.Request.RemoteEndPoint.Port,0);int delay=Delay,code=Code;bool stall=StallBody;
   if(delay>0)Thread.Sleep(delay);c.Response.StatusCode=Legacy&&c.Request.RawUrl.EndsWith("/mode/current")?404:code;c.Response.ContentType="application/json";c.Response.KeepAlive=true;
   var bytes=Encoding.UTF8.GetBytes(Broken?"invalid json":"{\"agentRunning\":true,\"currentMode\":\""+Mode+"\"}");
   c.Response.ContentLength64=bytes.Length;if(stall){c.Response.OutputStream.Write(bytes,0,1);c.Response.OutputStream.Flush();Thread.Sleep(3500);c.Response.OutputStream.Write(bytes,1,bytes.Length-1);}else c.Response.OutputStream.Write(bytes,0,bytes.Length);
   c.Response.Close();}catch{try{c.Response.Abort();}catch{}}});}}
 public void Dispose(){listener.Stop();listener.Close();worker.Join(2000);}
}
public sealed class PollFakeProcess:IDisposable{
 public static int Disposed;public static bool Throw;public static PollFakeProcess[] GetProcessesByName(string n){if(Throw)throw new Exception();return n=="present"?new[]{new PollFakeProcess(),new PollFakeProcess()}:new PollFakeProcess[0];}
 public void Dispose(){Disposed++;}
}
public sealed class PollCachedProcess:IDisposable{
 public static PollCachedProcess[] Next=new PollCachedProcess[0];public static int Enumerations;
 public int Id=99;public bool Exited,Disposed,Deny;public readonly Guid Generation=Guid.NewGuid();
 public PollCachedProcess SafeHandle{get{if(Deny)throw new Exception();return this;}}
 public bool HasExited{get{if(Deny)throw new Exception();return Exited;}}
 public static PollCachedProcess[] GetProcessesByName(string n){Enumerations++;return Next;}
 public void Dispose(){Disposed=true;}
}
'@
$server=$null;$savedAppData=$env:APPDATA
try{
 New-Item -ItemType Directory -Path $fixture|Out-Null
 $reservation=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$reservation.Start();$port=$reservation.LocalEndpoint.Port;$reservation.Stop()
 $server=[PollServer]::new($port);$uri="http://127.0.0.1:$port"
 $safety=[IO.File]::ReadAllText((Join-Path $assets 'desktop-safety.ps1'))
 # AST-linked production functions only: entrypoint and live mutex never run here.
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseInput($safety,[ref]$tokens,[ref]$errors)
 Assert ($errors.Count -eq 0) 'Production syntax'
 foreach($name in @('Invoke-AgentModeRequest','Get-AgentMode','Test-SteamProcessRunning')){
  $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))
 }
 $script:modeHttpClient=$null;$script:legacyModeAgentUri=$null
 Assert ((Get-AgentMode $uri) -eq 'Gaming') 'Actual loopback GET reads current mode'
 $latencies=@();foreach($i in 1..40){$c=[Diagnostics.Stopwatch]::StartNew();Assert ((Get-AgentMode $uri) -eq 'Gaming') "Pooled real request $i";$latencies+=$c.Elapsed.TotalMilliseconds}
 Assert ($server.Ports.Count -eq 1) 'Forty-one real requests reuse one TCP connection'
 Assert ($script:modeHttpClient.Timeout.TotalSeconds -eq 2) 'Production deadline stays two seconds'
 $server.Mode='Desktop';Assert ((Get-AgentMode $uri) -eq 'Desktop') 'Actual Desktop response disarms without stale mode caching'
 $server.Mode='Unknown';Assert ($null -eq (Get-AgentMode $uri)) 'Unknown real response is unavailable'
 $server.Mode='Gaming';$server.Broken=$true;Assert ($null -eq (Get-AgentMode $uri)) 'Malformed JSON is unavailable';$server.Broken=$false
 foreach($code in @(401,500,503)){$server.Code=$code;Assert ($null -eq (Get-AgentMode $uri)) "Actual HTTP $code is unavailable";Assert ($null -eq $script:legacyModeAgentUri) "Actual HTTP $code never falls back"}
 $server.Code=200;$server.Delay=3500;$c=[Diagnostics.Stopwatch]::StartNew();Assert ($null -eq (Get-AgentMode $uri)) 'Delayed headers cannot authorize transition';$headerDeadline=$c.Elapsed.TotalMilliseconds
 Assert ($headerDeadline -ge 1800 -and $headerDeadline -lt 3000) 'Delayed headers respect two-second deadline'
 $server.Delay=0;$server.StallBody=$true;$c=[Diagnostics.Stopwatch]::StartNew();Assert ($null -eq (Get-AgentMode $uri)) 'Delayed body cannot authorize transition';$bodyDeadline=$c.Elapsed.TotalMilliseconds
 Assert ($bodyDeadline -ge 1800 -and $bodyDeadline -lt 3000) 'Delayed body respects same two-second deadline'
 $server.StallBody=$false;Assert ((Get-AgentMode $uri) -eq 'Gaming') 'Pooled client recovers after cancelled requests'
 $server.Legacy=$true;Assert ((Get-AgentMode $uri) -eq 'Gaming') 'Actual 404 falls back to legacy status'
 $count=$server.Requests.Count;$server.Mode='Desktop';Assert ((Get-AgentMode $uri) -eq 'Desktop') 'Legacy state remains fresh'
 Assert ($server.Requests.Count -eq $count+1) 'Legacy capability cache sends only one request per tick'
 $server.Legacy=$false;$server.Mode='Gaming';$script:legacyModeAgentUri=$null
 $barText=[IO.File]::ReadAllText((Join-Path $assets 'xbox-gamebar.ps1'))
 $t=$null;$e=$null;$barAst=[Management.Automation.Language.Parser]::ParseInput($barText,[ref]$t,[ref]$e)
 $processFn=$barAst.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Test-HelperProcessRunning'},$true).Extent.Text
 . ([scriptblock]::Create($processFn))
 &{
  . ([scriptblock]::Create($processFn.Replace('[Diagnostics.Process]','[PollFakeProcess]')))
  Assert (Test-HelperProcessRunning 'present') 'Presence returns true for multiple matches'
  Assert ([PollFakeProcess]::Disposed -eq 2) 'Every matching Process is disposed even with early true result'
  Assert (-not (Test-HelperProcessRunning 'absent')) 'Absence remains false'
  [PollFakeProcess]::Throw=$true;Assert (-not (Test-HelperProcessRunning 'present')) 'Enumeration failure stays false';[PollFakeProcess]::Throw=$false
 }
 Assert (Test-HelperProcessRunning 'powershell') 'Production process check observes real fixture host'
 Assert (-not (Test-HelperProcessRunning ('no-process-'+[Guid]::NewGuid().ToString('N')))) 'Production process check rejects nonexistent process'
 $cachedFn=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Test-SteamProcessRunning'},$true).Extent.Text
 &{
  . ([scriptblock]::Create($cachedFn.Replace('[Diagnostics.Process]','[PollCachedProcess]')))
  $script:steamProcess=$null;$old=[PollCachedProcess]::new();$other=[PollCachedProcess]::new();[PollCachedProcess]::Next=@($old,$other)
  Assert (Test-SteamProcessRunning) 'Observed Steam instance is retained'
  Assert ($other.Disposed -and -not $old.Disposed) 'Additional enumerated processes are disposed'
  $calls=[PollCachedProcess]::Enumerations
  Assert ((Test-SteamProcessRunning) -and [PollCachedProcess]::Enumerations -eq $calls) 'Live handle is checked without repeated enumeration'
  $old.Exited=$true;$new=[PollCachedProcess]::new();[PollCachedProcess]::Next=@($new)
  Assert ((Test-SteamProcessRunning) -and $old.Disposed -and $script:steamProcess -eq $new) 'Steam restart rescans immediately in the same tick'
  Assert ($old.Id -eq $new.Id -and $old.Generation -ne $new.Generation) 'Reused PID retains only the replacement process handle identity'
  $new.Exited=$true;[PollCachedProcess]::Next=@()
  Assert (-not (Test-SteamProcessRunning)) 'Exited Steam with no replacement is missing immediately'
  $calls=[PollCachedProcess]::Enumerations;Assert (-not (Test-SteamProcessRunning)) 'No negative presence cache'
  Assert ([PollCachedProcess]::Enumerations -eq $calls+1) 'Missing process is rescanned every tick'
  $denied=[PollCachedProcess]::new();$denied.Deny=$true;[PollCachedProcess]::Next=@($denied)
  Assert ((Test-SteamProcessRunning) -and $null -eq $script:steamProcess -and $denied.Disposed) 'Elevated or inaccessible Steam preserves presence without retaining an unverified handle'
 }
 # Prove the production cached .NET handle observes a real disposable process exit.
 $cmd=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\cmd.exe') -ArgumentList '/c ping -n 30 127.0.0.1 > nul' -WindowStyle Hidden -PassThru;$children.Add($cmd)
 [void]$cmd.SafeHandle;$script:steamProcess=$cmd
 Assert (Test-SteamProcessRunning) 'Real held process handle reports alive'
 $cmd.Kill();[void]$cmd.WaitForExit(3000)
 $script:steamProcess=$cmd
 [void](Test-SteamProcessRunning)
 Assert ($script:steamProcess -ne $cmd) 'Real exited handle is discarded and actual Steam is rescanned'
 [void]$children.Remove($cmd) # The production function has disposed this exited object.
 if($null -ne $script:steamProcess){$script:steamProcess.Dispose();$script:steamProcess=$null}
 $barLoop=$barAst.Find({param($n)$n -is [Management.Automation.Language.WhileStatementAst] -and $n.Extent.Text.StartsWith('while ($true)')},$true).Extent.Text
 foreach($scenario in @('XboxExit','ControllerOverride','NoGame','RetryWrite')){
  &{
   $script:tick=0;$script:writes=@();$script:hookCalls=0;$script:writeAttempt=0
   $currentlyOn=$scenario -eq 'ControllerOverride'
   function Test-GamingModeOptOut {return $script:tick -ge 2}
   function Test-SteamControllerProfileActive {return $scenario -eq 'ControllerOverride'}
   function Test-HelperProcessRunning([string]$Name){
    if($Name -ne 'UWPHook'){throw 'Legacy Game Bar detector changed'}
    $script:hookCalls++;return ($scenario -eq 'RetryWrite' -or ($scenario -eq 'XboxExit' -and $script:tick -eq 0))
   }
   function Test-XboxSessionRunning { return Test-HelperProcessRunning 'UWPHook' }
   function Get-GameBarState {return 0}
   function Set-GameBar([int]$Value){$script:writes+=$Value;$script:writeAttempt++;return -not ($scenario -eq 'RetryWrite' -and $script:writeAttempt -eq 1)}
   function Start-Sleep([int]$Seconds){if($Seconds -ne 1){throw 'Game Bar cadence changed'};$script:tick++}
   function Write-Log([string]$Message){}
   . ([scriptblock]::Create($barLoop))
   $expected=switch($scenario){XboxExit{'1,0,0'} ControllerOverride{'0,0'} NoGame{'0'} RetryWrite{'1,1,0'}}
   Assert (($script:writes -join ',') -eq $expected) ("Production Game Bar $scenario transitions and optout preserved")
   if($scenario -eq 'ControllerOverride'){Assert ($script:hookCalls -eq 0) 'Active controller profile still bypasses UWPHook detection'}
  }
 }
 $samples=@()
 # Full scripts, same one-second cadence, four isolated processes. Registry writes
 # become Temp spies; namespaces/APPDATA/port/process name are isolated.
 $generations=if(Test-Path -LiteralPath (Join-Path $before 'desktop-safety.ps1')){@('before','after')}else{@('after')}
 foreach($generation in $generations){foreach($role in @('xbox-gamebar.ps1','desktop-safety.ps1')){
  $root=if($generation -eq 'before'){$before}else{$assets};$src=[IO.File]::ReadAllText((Join-Path $root $role))
  $dir=Join-Path $fixture ($generation+'-'+$role);New-Item -ItemType Directory -Path (Join-Path $dir 'GamingMode') -Force|Out-Null
  [IO.File]::WriteAllText((Join-Path $dir 'GamingMode\config.json'),('{"safety":{"apiPort":'+$port+'}}'))
  $src=$src.Replace('Local\Playhub.GamingMode.',('Local\Playhub.PollFixture.'+[Guid]::NewGuid().ToString('N')+'.')).Replace('Global\PlayhubGamingModeDesktopSafety',('Local\Playhub.PollFixture.'+[Guid]::NewGuid().ToString('N')))
  if($role -eq 'xbox-gamebar.ps1'){
   $t=$null;$e=$null;$barAst=[Management.Automation.Language.Parser]::ParseInput($src,[ref]$t,[ref]$e)
   foreach($name in @('Set-GameBar','Get-GameBarState')){$node=$barAst.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    $replace=if($name -eq 'Set-GameBar'){'function Set-GameBar([int]$value) { return $true }'}else{'function Get-GameBarState { return 0 }'};$src=$src.Replace($node.Extent.Text,$replace)}
   $src='function Get-AppxPackage { return $null }'+"`r`n"+$src
  }else{
   $src=$src.Replace("'steam'","'powershell'").Replace('Get-Process steam ','Get-Process powershell ')
  }
  $src="`$env:APPDATA = '$dir'`r`n"+$src
  $path=Join-Path $dir 'fixture.ps1';[IO.File]::WriteAllText($path,$src,[Text.UTF8Encoding]::new($false))
  $p=Start-Process -FilePath $shell -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$path+'"') -WindowStyle Hidden -PassThru
  $children.Add($p);$samples+=@{Generation=$generation;Role=$role;Process=$p;Directory=$dir}
 }}
 Start-Sleep -Seconds 4
 foreach($s in $samples){$s.Process.Refresh();Assert (-not $s.Process.HasExited) ($s.Generation+' '+$s.Role+' full script entered isolated loop');$s.Cpu=$s.Process.TotalProcessorTime.TotalSeconds}
 $sampleClock=[Diagnostics.Stopwatch]::StartNew();Start-Sleep -Seconds 30;$seconds=$sampleClock.Elapsed.TotalSeconds
 $metrics=@();foreach($s in $samples){$s.Process.Refresh();$delta=$s.Process.TotalProcessorTime.TotalSeconds-$s.Cpu;$metrics+=@{Generation=$s.Generation;Role=$s.Role;CpuSeconds=$delta;SampleSeconds=$seconds;CpuPercentOneLogicalCore=100*$delta/$seconds;PrivateBytes=$s.Process.PrivateMemorySize64}}
 # Normal production lifecycle exit; no fixture kill used except failure cleanup.
 foreach($s in $samples){if($s.Role -eq 'xbox-gamebar.ps1'){[IO.File]::WriteAllText((Join-Path $s.Directory 'GamingMode\disabled-by-user'),'fixture optout')}}
 $server.Mode='Desktop'
 foreach($s in $samples){Assert ($s.Process.WaitForExit(5000) -and $s.Process.ExitCode -eq 0) ($s.Generation+' '+$s.Role+' exits through production optout/Desktop lifecycle')}
 [pscustomobject]@{Checks=$checks;RequestMaxMilliseconds=($latencies|Measure-Object -Maximum).Maximum;HeaderTimeoutMilliseconds=$headerDeadline;BodyTimeoutMilliseconds=$bodyDeadline;Metrics=$metrics;Scope='Only isolated script copies, APPDATA, server, namespaces and registry spies. No live process or UI mutation.'}|ConvertTo-Json -Depth 5
}finally{
 if($null -ne $script:modeHttpClient){$script:modeHttpClient.Dispose();$script:modeHttpClient=$null}
 if($null -ne $server){$server.Dispose()}
 foreach($p in $children){if(-not $p.HasExited){$p.Kill();[void]$p.WaitForExit(3000)};$p.Dispose()}
 $env:APPDATA=$savedAppData
 $resolved=[IO.Path]::GetFullPath($fixture);if($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf).StartsWith('Playhub-helper-poll-')){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
