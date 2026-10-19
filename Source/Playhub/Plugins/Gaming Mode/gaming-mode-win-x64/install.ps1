param(
  [string]$SourceDir = "",
  [switch]$NoStartupTask
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
  if (Test-Path (Join-Path $PSScriptRoot "GamingMode.exe")) {
    $SourceDir = $PSScriptRoot
  }
  elseif (Test-Path (Join-Path $RepoRoot "artifacts\release\gaming-mode\GamingMode.exe")) {
    $SourceDir = Join-Path $RepoRoot "artifacts\release\gaming-mode"
  }
  else {
    $SourceDir = Join-Path $RepoRoot "artifacts\app"
  }
}

$SourceDir = (Resolve-Path $SourceDir).Path
$ExeSource = Join-Path $SourceDir "GamingMode.exe"
if (-not (Test-Path $ExeSource)) {
  throw "GamingMode.exe was not found in $SourceDir. Run scripts\build.ps1 first or pass -SourceDir."
}

$InstallDir = Join-Path $env:LOCALAPPDATA "GamingMode"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

function Resolve-GamingModeInstallIdentity([string]$Executable) {
  if (-not [IO.Path]::IsPathRooted($Executable) -or
      -not [string]::Equals([IO.Path]::GetFileName($Executable), 'GamingMode.exe', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The installed Gaming Mode executable identity is invalid.'
  }
  # A filesystem link is not evidence of ownership. Packaged-app path redirection
  # is resolved by the read handle below, without guessing any package directory.
  for ($part = [IO.Path]::GetFullPath($Executable); $part; $part = [IO.Path]::GetDirectoryName($part)) {
    if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
      throw 'A linked Gaming Mode installation path is refused.'
    }
  }
  if (-not ('PlayhubGamingModeInstall.ReadIdentity' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace PlayhubGamingModeInstall {
    public sealed class Identity { public string PhysicalPath; public uint Volume; public ulong FileIndex; }
    public static class ReadIdentity {
        [StructLayout(LayoutKind.Sequential)] private struct Info {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            public uint Serial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
        public static Identity Read(string path) {
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                Info info;
                if (!GetFileInformationByHandle(stream.SafeFileHandle, out info)) throw new System.ComponentModel.Win32Exception();
                var name = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(stream.SafeFileHandle, name, (uint)name.Capacity, 0);
                if (length == 0 || length >= name.Capacity) throw new System.ComponentModel.Win32Exception();
                string physical = name.ToString();
                if (physical.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) physical = @"\\" + physical.Substring(8);
                else if (physical.StartsWith(@"\\?\", StringComparison.Ordinal)) physical = physical.Substring(4);
                return new Identity { PhysicalPath = Path.GetFullPath(physical), Volume = info.Serial, FileIndex = ((ulong)info.IndexHigh << 32) | info.IndexLow };
            }
        }
    }
}
'@
  }
  return [PlayhubGamingModeInstall.ReadIdentity]::Read([IO.Path]::GetFullPath($Executable))
}

function Test-GamingModeInstallProcess([string]$ProcessPath, [int]$ProcessSessionId, [int]$InstallerSessionId, $ExpectedIdentity) {
  if ($null -eq $ExpectedIdentity -or $ProcessSessionId -ne $InstallerSessionId -or [string]::IsNullOrWhiteSpace($ProcessPath)) { return $false }
  try {
    $candidate = Resolve-GamingModeInstallIdentity $ProcessPath
    return [string]::Equals($candidate.PhysicalPath, $ExpectedIdentity.PhysicalPath, [StringComparison]::OrdinalIgnoreCase) -and
      $candidate.Volume -eq $ExpectedIdentity.Volume -and $candidate.FileIndex -eq $ExpectedIdentity.FileIndex
  } catch { return $false }
}

$Target = Join-Path $InstallDir "GamingMode.exe"
$ExpectedIdentity = $null
# Existing targets must resolve before any stop/copy operation. An absent target
# never authorizes stopping another installation's agent.
if (Test-Path -LiteralPath $Target) { $ExpectedIdentity = Resolve-GamingModeInstallIdentity $Target }

# FERMARE L'AGENTE PRIMA DI SOVRASCRIVERLO.
#
# Chiedere l'uscita non basta: fra il momento in cui il processo termina e
# quello in cui Windows rilascia il file passa un istante, e la copia arrivava
# proprio li' in mezzo. Ne usciva un errore rosso pieno di righe che non
# significava niente per chi installa, e per giunta durante un'installazione
# andata a buon fine.
#
# Qui si aspetta di poter davvero scrivere sul file prima di provarci.
Get-Process -Name "GamingMode" -ErrorAction SilentlyContinue | ForEach-Object {
  try {
    if (-not (Test-GamingModeInstallProcess $_.Path $_.SessionId ([Diagnostics.Process]::GetCurrentProcess().SessionId) $ExpectedIdentity)) { return }
    if (-not $_.CloseMainWindow()) {
      $_.Kill()
    }
    elseif (-not $_.WaitForExit(3000)) {
      $_.Kill()
    }
    $_.WaitForExit(5000) | Out-Null
  }
  catch {
  }
}

for ($attempt = 0; $attempt -lt 40; $attempt++) {
  if (-not (Test-Path $Target)) { break }
  try {
    # Se si apre in scrittura, nessuno lo sta piu' tenendo.
    $handle = [System.IO.File]::Open($Target, 'Open', 'Write', 'None')
    $handle.Close()
    break
  }
  catch {
    Start-Sleep -Milliseconds 250
  }
}

# Anche cosi' puo' capitare che un antivirus stia ancora leggendo i file
# appena scritti: qualche tentativo, in silenzio, e solo alla fine si parla.
$copied = $false
$copyError = $null
for ($attempt = 0; $attempt -lt 5; $attempt++) {
  try {
    Copy-Item -Path (Join-Path $SourceDir "*") -Destination $InstallDir -Recurse -Force -ErrorAction Stop
    $copied = $true
    break
  }
  catch {
    # L'eccezione va conservata: senza di essa il messaggio finale indovinava
    # la causa ("un altro programma tiene i file") anche quando il vero errore
    # era un disco pieno o un permesso negato.
    $copyError = $_
    Start-Sleep -Milliseconds 500
  }
}

if (-not $copied) {
  $reason = if ($null -ne $copyError) { $copyError.Exception.Message } else { "causa sconosciuta" }
  Write-Error ("Copia di Gaming Mode da '{0}' a '{1}' non riuscita: {2}" -f $SourceDir, $InstallDir, $reason)
  exit 1
}

# Remove the "mark of the web" so Windows doesn't show the "unknown publisher"
# security prompt every time the agent is launched.
Get-ChildItem -Path $InstallDir -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

$Exe = Join-Path $InstallDir "GamingMode.exe"
$Icon = Join-Path $InstallDir "assets\logo.ico"

# Playhub integrates Gaming Mode. The standalone app must NOT be exposed:
# remove any Desktop / Start Menu shortcuts left by older installs.
$DesktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Gaming Mode.lnk"
$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Gaming Mode"
Remove-Item -Path $DesktopShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -Path $StartMenuDir -Recurse -Force -ErrorAction SilentlyContinue

$Shell = New-Object -ComObject WScript.Shell

if (-not $NoStartupTask) {
  $StartupShortcut = Join-Path ([Environment]::GetFolderPath("Startup")) "Gaming Mode Agent.lnk"
  $Startup = $Shell.CreateShortcut($StartupShortcut)
  $Startup.TargetPath = $Exe
  $Startup.Arguments = "agent --boot"
  $Startup.WorkingDirectory = $InstallDir
  $Startup.Description = "Start Gaming Mode Agent"
  $Startup.WindowStyle = 7
  if (Test-Path $Icon) {
    $Startup.IconLocation = $Icon
  }
  $Startup.Save()
}

# Only this explicit installer re-enables a component removed by its owner.
$DisabledMarker = Join-Path $env:APPDATA 'GamingMode\disabled-by-user'
if (Test-Path -LiteralPath $DisabledMarker) { Remove-Item -LiteralPath $DisabledMarker -Force }
Start-Process -FilePath $Exe -ArgumentList "agent" -WindowStyle Hidden -WorkingDirectory $InstallDir
$AgentReady = $false
for ($i = 0; $i -lt 20; $i++) {
  Start-Sleep -Milliseconds 250
  try {
    $Health = Invoke-WebRequest -Uri "http://127.0.0.1:47991/health" -UseBasicParsing -TimeoutSec 1
    if ($Health.StatusCode -eq 200) {
      $AgentReady = $true
      break
    }
  }
  catch {
  }
}

Write-Host "Installed Gaming Mode to $InstallDir"
if ($AgentReady) {
  Write-Host "Local agent is running on http://127.0.0.1:47991"
}
else {
  Write-Host "WARNING: Local agent did not answer on http://127.0.0.1:47991"
}
