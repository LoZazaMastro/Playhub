# Watcher Xbox Game Bar di Playhub.
# Lanciato dall'agente SOLO in Gaming Mode (processo personalizzato), quando il
# toggle "Xbox Game Bar" è attivo.
#
# Scopo: il QAM di Steam non si disegna sopra i giochi Xbox/MS Store (app UWP).
# Per quei giochi si usa la Xbox Game Bar. Ma "Apri Game Bar dal controller" va
# tenuta SPENTA di norma (dà fastidio alla navigazione in Big Picture), quindi la
# accendiamo SOLO mentre gira un gioco Xbox e la rispegniamo alla chiusura.
#
# Le sessioni --uwp pubblicano l'identita del gioco su una pipe autenticata.
# I vecchi collegamenti UWPHook restano compatibili; gli altri wrapper sono esclusi.

$ErrorActionPreference = 'SilentlyContinue'

function Test-GamingModeOptOut {
    return Test-Path -LiteralPath (Join-Path $env:APPDATA 'GamingMode\disabled-by-user')
}
if (Test-GamingModeOptOut) { return }

# Local\ scopes the mutex to this Windows session; the SID separates users.
function Enter-GamingModeHelper([string]$Role) {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $mutex = [Threading.Mutex]::new($false, ('Local\Playhub.GamingMode.' + $Role + '.' + $sid))
    try {
        $owned = $false
        try { $owned = $mutex.WaitOne(0) }
        catch [Threading.AbandonedMutexException] { $owned = $true }
        if ($owned) { return $mutex }
    }
    catch { $mutex.Dispose(); throw }
    $mutex.Dispose()
    return $null
}

$helperMutex = Enter-GamingModeHelper 'XboxGameBar'
if ($null -eq $helperMutex) { return }
try {

$gameBarKey = 'HKCU:\Software\Microsoft\GameBar'
$gameBarValue = 'UseNexusForGameBarEnabled'

$logMain = Join-Path $env:APPDATA 'GamingMode\playhub-gamebar.log'

# Cap del log: oltre ~200 KB si riparte da capo.
try {
    if ((Test-Path -LiteralPath $logMain) -and ((Get-Item -LiteralPath $logMain).Length -gt 200KB)) {
        Remove-Item -LiteralPath $logMain -Force
    }
}
catch {
}

function Write-Log([string]$message) {
    $line = "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) $message"
    try { Add-Content -LiteralPath $logMain -Value $line -Encoding UTF8 } catch {}
}

function Get-GameBarState {
    try {
        return [int](Get-ItemProperty -LiteralPath $gameBarKey -Name $gameBarValue -ErrorAction Stop).$gameBarValue
    }
    catch {
        return 0
    }
}

function Set-GameBar([int]$value) {
    try {
        if (-not (Test-Path -LiteralPath $gameBarKey)) {
            New-Item -Path $gameBarKey -Force -ErrorAction Stop | Out-Null
        }
        Set-ItemProperty -LiteralPath $gameBarKey -Name $gameBarValue -Value $value -Type DWord -ErrorAction Stop
        $observed = [int](Get-ItemProperty -LiteralPath $gameBarKey -Name $gameBarValue -ErrorAction Stop).$gameBarValue
        if ($observed -ne $value) {
            Write-Log "Game Bar setting rejected: requested=$value observed=$observed"
            return $false
        }
        $notified = Send-GameBarSettingChange
        Write-Log "Game Bar setting verified: requested=$value observed=$observed notification=$notified"
        return $true
    }
    catch {
        Write-Log "ERRORE scrittura registro ($value): $_"
        return $false
    }
}

function Test-HelperProcessRunning([string]$Name) {
    $processes = @()
    try {
        $processes = [Diagnostics.Process]::GetProcessesByName($Name)
        return $processes.Length -gt 0
    }
    catch { return $false }
    finally {
        foreach ($process in $processes) { $process.Dispose() }
    }
}

function Test-SteamControllerProfileActive {
    $sidecar = ('vii' + 'per')
    return Test-HelperProcessRunning $sidecar
}

function Send-GameBarSettingChange {
    if (-not ('Playhub.GameBar.SessionNative' -as [type])) { return $false }
    return [Playhub.GameBar.SessionNative]::NotifySettingChange([IntPtr]0xffff)
}

function Initialize-XboxSessionNative {
    if ('Playhub.GameBar.SessionNative' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
namespace Playhub.GameBar {
 public sealed class ProcessLease : IDisposable {
  internal IntPtr Handle;
  public uint Pid, Session; public long Birth; public string Image, Package;
  public bool Alive { get { uint code; return Handle!=IntPtr.Zero && SessionNative.GetExitCodeProcess(Handle,out code) && code==259; } }
  public void Dispose(){if(Handle!=IntPtr.Zero){SessionNative.CloseHandle(Handle);Handle=IntPtr.Zero;}}
 }
 public static class SessionNative {
  public static bool NotifySettingChange(IntPtr destination) {
   UIntPtr result;
   return SendMessageTimeout(destination,0x1a,UIntPtr.Zero,"GameBar",0x22,10,out result)!=IntPtr.Zero;
  }
  public static ProcessLease Open(uint pid) {
   IntPtr h=OpenProcess(0x1000,false,pid);if(h==IntPtr.Zero)return null;
   try {
    uint code,session,n=32768;long birth,exit,kernel,user;
    if(!GetExitCodeProcess(h,out code)||code!=259||!GetProcessTimes(h,out birth,out exit,out kernel,out user)||!ProcessIdToSessionId(pid,out session))return null;
    var image=new StringBuilder((int)n);if(!QueryFullProcessImageName(h,0,image,ref n))return null;
    n=512;var family=new StringBuilder((int)n);string package=GetPackageFamilyName(h,ref n,family)==0?family.ToString():"";
    var result=new ProcessLease{Handle=h,Pid=pid,Session=session,Birth=birth,Image=image.ToString(),Package=package};h=IntPtr.Zero;return result;
   } finally {if(h!=IntPtr.Zero)CloseHandle(h);}
  }
  public static uint CurrentSession { get { uint session; return ProcessIdToSessionId(GetCurrentProcessId(),out session)?session:uint.MaxValue; } }
  public static string ReadIdentity(string name,uint expectedPid) {
   using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.In,PipeOptions.Asynchronous)) {
    pipe.Connect(50);uint server;
    if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out server)||server!=expectedPid)return null;
    var bytes=new byte[4096];int length=0;var deadline=System.Diagnostics.Stopwatch.StartNew();
    while(length<bytes.Length) {
     int remaining=150-(int)deadline.ElapsedMilliseconds;if(remaining<=0)return null;
     var read=pipe.ReadAsync(bytes,length,bytes.Length-length);
     if(!read.Wait(remaining))return null;
     int count=read.Result;if(count==0)break;length+=count;
     if(Array.IndexOf(bytes,(byte)'\n',0,length)>=0)break;
    }
    return length>0&&length<bytes.Length?Encoding.UTF8.GetString(bytes,0,length):null;
   }
  }
  [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
  [DllImport("kernel32.dll")] internal static extern bool GetExitCodeProcess(IntPtr process,out uint code);
  [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr process,out long birth,out long exit,out long kernel,out long user);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder image,ref uint length);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetPackageFamilyName(IntPtr process,ref uint length,StringBuilder family);
  [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
  [DllImport("kernel32.dll")] static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
  [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(uint pid,out uint session);
  [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,UIntPtr wParam,string lParam,uint flags,uint timeout,out UIntPtr result);
 }
}
'@ -ErrorAction Stop
}

function Get-XboxSessionPipes {
    try { return [IO.Directory]::GetFiles('\\.\pipe\', 'Playhub.UwpIdentity.*') }
    catch { return @() }
}

function Open-XboxSessionProcess([uint32]$ProcessId) {
    return [Playhub.GameBar.SessionNative]::Open($ProcessId)
}

function Read-XboxSessionIdentity([string]$PipeName, [uint32]$WrapperPid) {
    return [Playhub.GameBar.SessionNative]::ReadIdentity($PipeName, $WrapperPid)
}

function Clear-XboxSession {
    if ($script:xboxSession) {
        $script:xboxSession.Wrapper.Dispose()
        $script:xboxSession.Game.Dispose()
        $script:xboxSession = $null
    }
}

function Test-XboxSessionRunning {
    if ($script:xboxSession) {
        if ($script:xboxSession.Wrapper.Alive -and $script:xboxSession.Game.Alive) { return $true }
        Clear-XboxSession
    }
    $count = 0
    foreach ($path in (Get-XboxSessionPipes)) {
        $name = [IO.Path]::GetFileName($path)
        if ($name -notmatch '^Playhub\.UwpIdentity\.(\d+)\.([0-9A-Fa-f]{16})$') { continue }
        if (++$count -gt 8) { break }
        $wrapper = $null; $game = $null
        try {
            $wrapperPid = [uint32]$Matches[1]
            $birth = [Convert]::ToInt64($Matches[2], 16)
            $wrapper = Open-XboxSessionProcess $wrapperPid
            if (-not $wrapper -or -not $wrapper.Alive -or $wrapper.Birth -ne $birth -or
                $wrapper.Session -ne $script:xboxSessionWindowsSession -or
                -not [string]::Equals($wrapper.Image, $script:xboxSessionHelper, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $payload = Read-XboxSessionIdentity $name $wrapperPid
            if (-not $payload) { continue }
            $identity = $payload | ConvertFrom-Json -ErrorAction Stop
            if ([uint32]$identity.WrapperPid -ne $wrapperPid -or [long]$identity.WrapperBirth -ne $birth -or
                [uint32]$identity.GamePid -eq 0 -or [long]$identity.GameBirth -le 0 -or
                $identity.Aumid -notmatch '^[^!]+![^!]+$') { continue }
            $game = Open-XboxSessionProcess ([uint32]$identity.GamePid)
            if (-not $game -or -not $game.Alive -or $game.Birth -ne [long]$identity.GameBirth -or
                $game.Session -ne $wrapper.Session -or
                -not [string]::Equals($game.Image, [string]$identity.Executable, [StringComparison]::OrdinalIgnoreCase) -or
                -not [string]::Equals($game.Package, [string]$identity.PackageFamily, [StringComparison]::OrdinalIgnoreCase) -or
                -not [string]::Equals($game.Package, $identity.Aumid.Split('!')[0], [StringComparison]::OrdinalIgnoreCase)) { continue }
            if (-not $wrapper.Alive) { continue }
            $script:xboxSession = @{ Wrapper = $wrapper; Game = $game }
            $wrapper = $null; $game = $null
            return $true
        }
        catch { }
        finally {
            if ($wrapper) { $wrapper.Dispose() }
            if ($game) { $game.Dispose() }
        }
    }
    return Test-HelperProcessRunning 'UWPHook'
}

$script:xboxSession = $null
$script:xboxSessionHelper = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\Playhub.GameSession.exe'))
Initialize-XboxSessionNative
$script:xboxSessionWindowsSession = [Playhub.GameBar.SessionNative]::CurrentSession

Write-Log '--- Watcher Xbox Game Bar avviato ---'

# Diagnostica: la Xbox Game Bar è installata? (se manca, la feature è inutile)
try {
    $pkg = Get-AppxPackage -Name 'Microsoft.XboxGamingOverlay' -ErrorAction SilentlyContinue
    if ($pkg) { Write-Log "Xbox Game Bar installata (versione $($pkg.Version))." }
    else { Write-Log 'ATTENZIONE: pacchetto Xbox Game Bar NON trovato.' }
}
catch {
}
Write-Log "Stato iniziale 'apri Game Bar dal controller': $(Get-GameBarState)"

# All'avvio: spegni (nessun gioco Xbox ancora in esecuzione).
Set-GameBar 0 | Out-Null
$currentlyOn = $false
Write-Log "Game Bar controller: OFF (avvio, nessun gioco Xbox)."

# La presenza verificata viene mantenuta con handle fino alla fine della sessione.
while ($true) {
    if (Test-GamingModeOptOut) {
        Set-GameBar 0 | Out-Null
        Write-Log 'Gaming Mode removed: Game Bar helper stopped.'
        break
    }
    if (Test-SteamControllerProfileActive) {
        if ($currentlyOn -or (Get-GameBarState) -ne 0) {
            Set-GameBar 0 | Out-Null
            $currentlyOn = $false
            Write-Log 'Steam Controller attivo -> Game Bar controller: OFF.'
        }
        Start-Sleep -Seconds 1
        continue
    }

    $xboxRunning = Test-XboxSessionRunning

    if ($xboxRunning -and (-not $currentlyOn -or (Get-GameBarState) -ne 1)) {
        if (Set-GameBar 1) {
            $currentlyOn = $true
            Write-Log 'Sessione Xbox verificata -> Game Bar controller: ON.'
        }
    }
    elseif (-not $xboxRunning -and $currentlyOn) {
        if (Set-GameBar 0) {
            $currentlyOn = $false
            Write-Log 'Gioco Xbox chiuso -> Game Bar controller: OFF.'
        }
    }

    Start-Sleep -Seconds 1
}

}
finally {
    try {
        if (Get-Command Set-GameBar -ErrorAction SilentlyContinue) { Set-GameBar 0 | Out-Null }
        if (Get-Command Clear-XboxSession -ErrorAction SilentlyContinue) { Clear-XboxSession }
    }
    finally { try { $helperMutex.ReleaseMutex() } finally { $helperMutex.Dispose() } }
}
