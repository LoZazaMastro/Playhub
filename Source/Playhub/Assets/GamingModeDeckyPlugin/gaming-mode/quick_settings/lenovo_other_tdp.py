"""Original Windows Lenovo Other-Mode TDP transactions for documented Go S/Go 2.

Protocol facts: Linux 7e1392a42ea288d70a266f6553fe06f57f8647ea,
Documentation/wmi/devices/lenovo-wmi-other.rst and drivers/platform/x86/lenovo/
wmi-{other,capdata,helpers,gamezone}. No HC implementation or driver is used.
"""
import base64
import copy
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import threading

RAILS = ("spl", "sppt", "fppt")
FEATURES = {"spl": 2, "sppt": 1, "fppt": 3}
IDENTITIES = {"83L3": ("Legion Go S 8ARP1",),
              "83N6": ("Legion Go S 8APU1", "Legion Go S 8ARP1"),
              "83N0": ("Legion Go 8ASP2",), "83N1": ("Legion Go 8AHP2",)}
_TOKEN = re.compile(r"[a-f0-9]{64}\Z")


def supports_identity(value):
    if not isinstance(value, dict) or value.get("manufacturer") != "LENOVO":
        return False
    versions = IDENTITIES.get(value.get("model"), ())
    return value.get("version") in versions or value.get("version") in tuple("Lenovo " + v for v in versions)


def valid_limits(value):
    return (isinstance(value, dict) and set(value) == set(RAILS)
            and all(type(value[k]) is int and 0 < value[k] <= 0xffffffff for k in RAILS))


def valid_bounds(value):
    return (isinstance(value, dict) and set(value) == set(RAILS)
            and all(isinstance(value[k], dict) and set(value[k]) == {"minimum", "maximum", "step", "default", "capability", "capabilityId", "valuePage"}
                    and all(type(value[k][f]) is int and 0 <= value[k][f] <= 0xffffffff for f in value[k])
                    and 0 < value[k]["minimum"] <= value[k]["default"] <= value[k]["maximum"]
                    and value[k]["step"] > 0 and value[k]["capability"] & 3 == 3
                    and (value[k]["default"] - value[k]["minimum"]) % value[k]["step"] == 0
                    and value[k]["capabilityId"] in ((1 << 24) | (FEATURES[k] << 16), (1 << 24) | (FEATURES[k] << 16) | (255 << 8))
                    and value[k]["valuePage"] in (0, 255) for k in RAILS))


def fits(limits, bounds):
    return (valid_limits(limits) and valid_bounds(bounds)
            and all(bounds[k]["minimum"] <= limits[k] <= bounds[k]["maximum"]
                    and (limits[k] - bounds[k]["minimum"]) % bounds[k]["step"] == 0 for k in RAILS)
            and limits["spl"] <= limits["sppt"] <= limits["fppt"])


_SCRIPT = r'''
param([Parameter(Mandatory=$true)][string]$RequestBase64)
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($RequestBase64))|ConvertFrom-Json
$clock=[Diagnostics.Stopwatch]::StartNew();$script:mutex=$null;$script:locked=$false
$script:identity=$null;$script:other=$null;$script:gamezone=$null;$script:token=$null
$script:features=[ordered]@{spl=2;sppt=1;fppt=3}
function DeviceIdentity {
    $b=Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS'
    $models=@{'83L3'=@('Legion Go S 8ARP1');'83N6'=@('Legion Go S 8APU1','Legion Go S 8ARP1');'83N0'=@('Legion Go 8ASP2');'83N1'=@('Legion Go 8AHP2')}
    $model=[string]$b.SystemProductName;$version=[string]$b.SystemVersion
    $allowed=@($models[$model]);$allowed+=@($allowed|ForEach-Object {'Lenovo '+$_})
    if([string]$b.SystemManufacturer -cne 'LENOVO' -or !$models.ContainsKey($model) -or $allowed -cnotcontains $version -or [string]::IsNullOrWhiteSpace([string]$b.BIOSVersion)) {throw 'device_unsupported'}
    return @([string]$b.SystemManufacturer,$model,$version,[string]$b.BIOSVersion)|ConvertTo-Json -Compress
}
function ParamIs($m,[string]$name,[string]$type,[bool]$asInput) {
    $p=$m.Parameters[$name];if($null -eq $p -or $p.CimType.ToString() -cne $type) {return $false}
    if($asInput) {return ($p.Qualifiers['In'].Value -eq $true -and $p.Qualifiers['Out'].Value -ne $true)}
    return ($p.Qualifiers['Out'].Value -eq $true -and $p.Qualifiers['In'].Value -ne $true)
}
function MethodIs($m,[int]$id,[string[]]$inputs,[string[]]$outputs) {
    if($null -eq $m -or $m.Parameters.Count -ne ($inputs.Count+$outputs.Count) -or
        $m.Qualifiers['Implemented'].Value -ne $true -or $m.Qualifiers['WmiMethodId'].Value -ne $id -or
        @($m.Qualifiers['Privileges'].Value|Where-Object {$_}).Count -ne 0) {return $false}
    foreach($name in $inputs) {if(!(ParamIs $m $name 'UInt32' $true)) {return $false}}
    foreach($name in $outputs) {if(!(ParamIs $m $name 'UInt32' $false)) {return $false}}
    return $true
}
function BaseSchema($c,[string]$name,[string]$guid) {
    try {return ($c.CimClassName -ceq $name -and $c.CimClassQualifiers['Dynamic'].Value -eq $true -and
        $c.CimClassQualifiers['Provider'].Value -ieq 'WmiProv' -and [guid]$c.CimClassQualifiers['guid'].Value -eq [guid]$guid -and
        $c.CimClassProperties['InstanceName'].CimType.ToString() -ceq 'String' -and
        $c.CimClassProperties['InstanceName'].Qualifiers['Key'].Value -eq $true -and
        $c.CimClassProperties['Active'].CimType.ToString() -ceq 'Boolean' -and
        @($c.CimClassProperties|Where-Object {$_.Qualifiers['Key'].Value -eq $true}).Count -eq 1)}catch{return $false}
}
function ClassesValid($other,$caps,$gamezone) {
    if(!(BaseSchema $other 'LENOVO_OTHER_METHOD' 'DC2A8805-3A8C-41BA-A6F7-092E0089CD3B') -or
        !(MethodIs $other.CimClassMethods['GetFeatureValue'] 17 @('IDs') @('value')) -or
        !(MethodIs $other.CimClassMethods['SetFeatureValue'] 18 @('IDs','value') @()) -or
        !(ModeSchema $gamezone) -or
        !(BaseSchema $caps 'LENOVO_CAPABILITY_DATA_01' '7A8F5407-CB67-4D6E-B547-39B3BE018154')) {return $false}
    foreach($name in @('IDs','Capability','DefaultValue','Step','MinValue','MaxValue')) {
        if($caps.CimClassProperties[$name].CimType.ToString() -cne 'UInt32') {return $false}
    }
    return $true
}
function ModeSchema($gamezone) {
    return ((BaseSchema $gamezone 'LENOVO_GAMEZONE_DATA' '887B54E3-DDDC-4B2C-8B88-68A26A8835D0') -and
        (MethodIs $gamezone.CimClassMethods['IsSupportSmartFan'] 43 @() @('Data')) -and
        (MethodIs $gamezone.CimClassMethods['SetSmartFanMode'] 44 @('Data') @()) -and
        (MethodIs $gamezone.CimClassMethods['GetSmartFanMode'] 45 @() @('Data')) -and
        (MethodIs $gamezone.CimClassMethods['GetThermalMode'] 55 @() @('Data')))
}
function Singleton([string]$name,$expected=$null) {
    $v=@(Get-CimInstance -Namespace root/wmi -ClassName $name -OperationTimeoutSec 1)
    if($v.Count -ne 1 -or $v[0].Active -isnot [bool] -or !$v[0].Active -or [string]::IsNullOrWhiteSpace([string]$v[0].InstanceName) -or
        $v[0].CimSystemProperties.ClassName -cne $name -or @('root/wmi','root\wmi') -inotcontains $v[0].CimSystemProperties.Namespace -or
        ($null -ne $expected -and ([string]$v[0].InstanceName -cne [string]$expected.InstanceName -or [string]$v[0].CimSystemProperties.ServerName -cne [string]$expected.CimSystemProperties.ServerName))) {throw 'instance_changed'}
    return $v[0]
}
function OpenProvider {
    $script:identity=DeviceIdentity
    $script:mutex=[Threading.Mutex]::new($false,'Global\Playhub.LenovoOtherTdp')
    try {$script:locked=$script:mutex.WaitOne(100)}catch [Threading.AbandonedMutexException] {$script:locked=$true}
    if(!$script:locked) {throw 'busy'}
    $g=Get-CimClass -Namespace root/wmi -ClassName LENOVO_GAMEZONE_DATA -OperationTimeoutSec 1
    if(!(ModeSchema $g)) {throw 'mode_schema_unknown'}
    $p=@(Get-CimInstance -Namespace root/wmi -ClassName __Win32Provider -Filter "Name='WmiProv'" -OperationTimeoutSec 1)
    if($p.Count -ne 1 -or [guid]$p[0].CLSID -ne [guid]'D2D588B5-D081-11D0-99E0-00C04FC2F8EC' -or $p[0].HostingModel -ine 'LocalSystemHost') {throw 'provider_unknown'}
    $r=Get-ItemProperty -LiteralPath 'Registry::HKEY_CLASSES_ROOT\CLSID\{D2D588B5-D081-11D0-99E0-00C04FC2F8EC}\InprocServer32'
    $path=[IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($r.'(default)'))
    if($path -ine [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'System32\wbem\wmiprov.dll'))) {throw 'provider_path_unknown'}
    $sig=Get-AuthenticodeSignature -LiteralPath $path
    if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {throw 'provider_signature_unknown'}
    $script:providerHash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $script:gamezone=Singleton 'LENOVO_GAMEZONE_DATA';$script:tdpSchema=$false;$script:other=$null
    try {
        $o=Get-CimClass -Namespace root/wmi -ClassName LENOVO_OTHER_METHOD -OperationTimeoutSec 1
        $c=Get-CimClass -Namespace root/wmi -ClassName LENOVO_CAPABILITY_DATA_01 -OperationTimeoutSec 1
        if(ClassesValid $o $c $g) {$script:other=Singleton 'LENOVO_OTHER_METHOD';$script:tdpSchema=$true}
    }catch {$script:tdpSchema=$false;$script:other=$null}
}
function UInt($v) {return ($v -is [uint32])}
function Modes {
    $a=Invoke-CimMethod -InputObject $script:gamezone -MethodName GetSmartFanMode -OperationTimeoutSec 1
    $b=Invoke-CimMethod -InputObject $script:gamezone -MethodName GetThermalMode -OperationTimeoutSec 1
    if(!(UInt $a.Data) -or !(UInt $b.Data) -or @(1,2,3,224,255) -notcontains [int]$a.Data -or @(1,2,3,224,255) -notcontains [int]$b.Data) {throw 'mode_unknown'}
    return @{mode=[int]$a.Data;realMode=[int]$b.Data}
}
function Power {
    if($null -eq ('PlayhubLenovoPowerStatus' -as [type])) {
        Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class PlayhubLenovoPowerStatus{[StructLayout(LayoutKind.Sequential)]public struct S{public byte ACLineStatus,BatteryFlag,BatteryLifePercent,SystemStatusFlag;public uint BatteryLifeTime,BatteryFullLifeTime;}[DllImport("kernel32.dll")]public static extern bool GetSystemPowerStatus(out S status);}'
    }
    $s=[PlayhubLenovoPowerStatus+S]::new()
    if(![PlayhubLenovoPowerStatus]::GetSystemPowerStatus([ref]$s) -or @(0,1) -notcontains [int]$s.ACLineStatus) {return -1}
    return [int]$s.ACLineStatus
}
function Competitor {
    return @(Get-CimInstance -ClassName Win32_Process -Filter "Name='LegionSpace.exe' OR Name='LegionSpace.Daemon.exe' OR Name='LegionService.exe' OR Name='HandheldCompanion.exe' OR Name='HandheldControlPanel.exe'" -OperationTimeoutSec 1).Count -ne 0
}
function Guard($context,[bool]$writing,[bool]$modeChange=$false) {
    if((DeviceIdentity) -cne $script:identity) {throw 'device_changed'}
    if(!$modeChange -and $script:tdpSchema) {$null=Singleton 'LENOVO_OTHER_METHOD' $script:other}
    $null=Singleton 'LENOVO_GAMEZONE_DATA' $script:gamezone
    $foreign=Competitor;$m=Modes;$power=Power
    if($m.mode -ne $context.mode -or $m.realMode -ne $context.realMode -or
        $power -ne $context.power -or $clock.ElapsedMilliseconds -gt 12000) {throw 'context_changed'}
    if($writing -and ((!$modeChange -and ($m.mode -ne 255 -or $m.realMode -ne 255)) -or $power -lt 0 -or $foreign)) {throw 'write_not_authorized'}
}
function FeatureId([string]$rail,[int]$page) {
    if(!$script:features.Contains($rail) -or @(0,1,2,3,224,255) -notcontains $page) {throw 'feature_forbidden'}
    return [uint32](0x01000000+65536*$script:features[$rail]+256*$page)
}
function ReadValue([string]$rail,[int]$page) {
    $r=Invoke-CimMethod -InputObject $script:other -MethodName GetFeatureValue -Arguments @{IDs=(FeatureId $rail $page)} -OperationTimeoutSec 1
    if(!(UInt $r.value) -or $r.value -eq 0) {throw 'value_unavailable'}
    return [long]$r.value
}
function BoundRow($row,[uint32]$id) {
    if($null -eq $row -or $row.Active -isnot [bool] -or !$row.Active -or $row.CimSystemProperties.ClassName -cne 'LENOVO_CAPABILITY_DATA_01' -or
        @('root/wmi','root\wmi') -inotcontains $row.CimSystemProperties.Namespace -or [string]::IsNullOrWhiteSpace([string]$row.InstanceName)) {return $false}
    foreach($name in @('IDs','Capability','DefaultValue','Step','MinValue','MaxValue')) {if(!(UInt $row.$name)) {return $false}}
    return ($row.IDs -eq $id -and ($row.Capability -band 3) -eq 3 -and $row.MinValue -gt 0 -and $row.Step -gt 0 -and
        $row.MinValue -le $row.DefaultValue -and $row.DefaultValue -le $row.MaxValue -and (($row.DefaultValue-$row.MinValue)%$row.Step) -eq 0)
}
function Bounds {
    $rows=@(Get-CimInstance -Namespace root/wmi -ClassName LENOVO_CAPABILITY_DATA_01 -OperationTimeoutSec 1)
    if($rows.Count -eq 0 -or $rows.Count -gt 256) {throw 'capabilities_unknown'}
    $result=[ordered]@{}
    foreach($rail in $script:features.Keys) {
        $row=$null;$id=0
        foreach($page in @(255,0)) {
            $id=FeatureId $rail $page;$matching=@($rows|Where-Object {$_.IDs -eq $id})
            if($matching.Count -gt 1) {throw 'capability_ambiguous'}
            if($matching.Count -eq 1) {
                if(BoundRow $matching[0] $id) {$row=$matching[0];break}
                # Only an explicitly unsupported page may fall through to page0.
                # A present, malformed/inactive range is not positive absence.
                if($matching[0].Active -isnot [bool] -or !$matching[0].Active -or !(UInt $matching[0].Capability) -or $matching[0].Capability -ne 0) {throw 'capability_invalid'}
            }
        }
        if($null -eq $row) {throw 'capability_unavailable'}
        $valuePage=$null
        foreach($page in @(255,0)) {try {$null=ReadValue $rail $page;$valuePage=$page;break}catch{}}
        if($null -eq $valuePage) {throw 'value_page_unknown'}
        $result[$rail]=@{minimum=[long]$row.MinValue;maximum=[long]$row.MaxValue;step=[long]$row.Step;default=[long]$row.DefaultValue;
            capability=[long]$row.Capability;capabilityId=[long]$id;valuePage=[int]$valuePage}
    }
    return $result
}
function Current($bounds,[int]$mode) {
    $v=[ordered]@{}
    foreach($rail in $script:features.Keys) {$page=$mode;if($bounds[$rail].valuePage -eq 0) {$page=0};$v[$rail]=ReadValue $rail $page}
    return $v
}
function LimitsValid($v) {
    if($null -eq $v -or @($v.PSObject.Properties.Name).Count -ne 3) {return $false}
    foreach($key in @('spl','sppt','fppt')) {if(($v.$key -isnot [int] -and $v.$key -isnot [long]) -or $v.$key -le 0 -or $v.$key -gt [uint32]::MaxValue) {return $false}}
    return ($v.spl -le $v.sppt -and $v.sppt -le $v.fppt)
}
function Fits($v,$bounds) {
    foreach($key in @('spl','sppt','fppt')) {if($v.$key -lt $bounds[$key].minimum -or $v.$key -gt $bounds[$key].maximum -or (($v.$key-$bounds[$key].minimum)%$bounds[$key].step) -ne 0) {return $false}}
    return ($v.spl -le $v.sppt -and $v.sppt -le $v.fppt)
}
function Same($a,$b) {foreach($key in @('spl','sppt','fppt')) {if($null -eq $a.$key -or $a.$key -ne $b.$key) {return $false}};return $true}
function BoundsStamp($b) {
    $v=@();foreach($rail in @('spl','sppt','fppt')) {$s=$b[$rail];$v+=,@($rail,$s.minimum,$s.maximum,$s.step,$s.default,$s.capability,$s.capabilityId,$s.valuePage)}
    return $v|ConvertTo-Json -Compress -Depth 5
}
function HashText([string]$text) {
    $h=[Security.Cryptography.SHA256]::Create();try {return -join($h.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))|ForEach-Object {$_.ToString('x2')})}finally{$h.Dispose()}
}
function WriteValue([string]$rail,[long]$value,$bounds) {
    if(!$script:features.Contains($rail) -or $value -lt $bounds[$rail].minimum -or $value -gt $bounds[$rail].maximum -or
        (($value-$bounds[$rail].minimum)%$bounds[$rail].step) -ne 0 -or ($bounds[$rail].capability -band 7) -ne 7) {throw 'limit_invalid'}
    $null=Invoke-CimMethod -InputObject $script:other -MethodName SetFeatureValue -Arguments @{IDs=(FeatureId $rail $bounds[$rail].valuePage);value=[uint32]$value} -OperationTimeoutSec 1
}
function WriteMode([int]$mode) {
    if(@(1,2,3,255) -notcontains $mode) {throw 'mode_forbidden'}
    $null=Invoke-CimMethod -InputObject $script:gamezone -MethodName SetSmartFanMode -Arguments @{Data=[uint32]$mode} -OperationTimeoutSec 1
}
function ConfirmMode([int]$mode) {
    $wait=[Diagnostics.Stopwatch]::StartNew()
    do {$m=Modes;if($m.mode -eq $mode -and $m.realMode -eq $mode) {return $true};Start-Sleep -Milliseconds 60}while($wait.ElapsedMilliseconds -lt 600)
    return $false
}
function Report {
    $context=Modes
    if($context.mode -ne $context.realMode) {throw 'mode_transition'}
    $support=Invoke-CimMethod -InputObject $script:gamezone -MethodName IsSupportSmartFan -OperationTimeoutSec 1
    if(!(UInt $support.Data) -or $support.Data -lt 1) {throw 'mode_interface_unknown'}
    $context.power=Power;$foreign=Competitor;$bounds=$null;$before=$null;$tdpAvailable=$false
    if($script:tdpSchema) {
        try {
            $bounds=Bounds;Guard $context $false
            $before=Current $bounds $context.mode;Guard $context $false
            $tdpAvailable=$true
        }catch {$bounds=$null;$before=$null}
    }
    # A failed optional Other/Cap01 read never becomes a TDP grant. The independent
    # GameZone proof is still required for a mode-only report/action.
    Guard $context $false $true
    if((Power) -ne $context.power -or $clock.ElapsedMilliseconds -gt 12000) {throw 'context_changed'}
    $deviceStamp=@($script:identity,[string]$script:gamezone.InstanceName,$script:providerHash)|ConvertTo-Json -Compress
    $deviceToken=HashText $deviceStamp
    $capsStamp='tdp-unavailable';if($tdpAvailable) {$capsStamp=@([string]$script:other.InstanceName,(BoundsStamp $bounds))|ConvertTo-Json -Compress}
    $script:token=HashText (@($deviceToken,$capsStamp,$context.mode,$context.realMode,$context.power,[long]$support.Data)|ConvertTo-Json -Compress)
    $writable=($tdpAvailable -and $context.mode -eq 255 -and $context.realMode -eq 255 -and $context.power -ge 0 -and !$foreign -and (Fits $before $bounds))
    if($tdpAvailable) {foreach($key in $script:features.Keys) {if(($bounds[$key].capability -band 7) -ne 7) {$writable=$false}}}
    $canMode=(@(1,2,3,255) -contains $context.mode -and $context.power -ge 0 -and !$foreign)
    $result=@{ok=$true;available=$true;code='';token=$script:token;deviceToken=$deviceToken;tdpAvailable=$tdpAvailable;limitsWatts=$before;boundsWatts=$bounds;mode=$context.mode;realMode=$context.realMode;
        externalPower=$(if($context.power -ge 0){[bool]$context.power}else{$null});writable=$writable;
        customModeAvailable=(@(1,2,3) -contains $context.mode -and $context.power -ge 0 -and !$foreign);
        modeEditable=$canMode;modeControlAvailable=$canMode;
        nativeUnit='W';changed=$false;recoveryRequired=$false}
    if(!$writable) {$result.code='read_only_context'}
    if(!$tdpAvailable) {$result.code='tdp_unavailable'}
    return $result
}
function Operate {
    $attempted=$false;$modeAttempted=$false;$oldMode=$null;$modeTarget=$null;$context=$null
    try {
        OpenProvider;$result=Report;$bounds=$result.boundsWatts;$before=$result.limitsWatts
        $context=@{mode=$result.mode;realMode=$result.realMode;power=$(if($null -eq $result.externalPower){-1}else{[int][bool]$result.externalPower})}
        if($request.action -eq 'snapshot') {return $result}
        if(@('custom','mode') -contains $request.action) {
            if($request.confirmed -isnot [bool] -or !$request.confirmed -or $request.token -cne $script:token -or
                $request.expectedMode -isnot [int] -or $request.expectedMode -ne $context.mode) {throw 'state_changed'}
            $modeTarget=255
            if($request.action -eq 'mode') {if($request.mode -isnot [int] -or @(1,2,3,255) -notcontains $request.mode) {throw 'mode_forbidden'};$modeTarget=[int]$request.mode}
            if(!$result.modeEditable) {throw 'mode_unavailable'}
            if($context.mode -eq $modeTarget) {Guard $context $true $true;return $result}
            Guard $context $true $true
            $oldMode=[int]$context.mode;$attempted=$true;$modeAttempted=$true;WriteMode $modeTarget
            if(!(ConfirmMode $modeTarget)) {throw 'mode_not_confirmed'}
            $afterContext=@{mode=$modeTarget;realMode=$modeTarget;power=$context.power};Guard $afterContext $true $true
            $after=Report;Guard $afterContext $true $true
            if($after.deviceToken -cne $result.deviceToken -or $after.mode -ne $modeTarget -or $after.realMode -ne $modeTarget) {throw 'mode_context_changed'}
            $after.changed=$true;return $after
        }
        if(!$result.writable -or @('set','restore') -notcontains $request.action -or $request.confirmed -isnot [bool] -or !$request.confirmed -or
            $request.token -cne $script:token -or !(LimitsValid $request.expected) -or !(Same $before $request.expected)) {throw 'state_changed'}
        $target=$request.limits;if($request.action -eq 'restore') {$target=$request.baseline}
        if(!(LimitsValid $target) -or !(Fits $target $bounds)) {throw 'target_out_of_bounds'}
        $expected=@{spl=$before.spl;sppt=$before.sppt;fppt=$before.fppt};$order=@()
        foreach($key in @('spl','sppt','fppt')) {if($target.$key -lt $before[$key]) {$order+=,$key}}
        foreach($key in @('fppt','sppt','spl')) {if($target.$key -gt $before[$key]) {$order+=,$key}}
        foreach($key in $order) {
            if((BoundsStamp (Bounds)) -cne (BoundsStamp $bounds)) {throw 'concurrent_change'}
            Guard $context $true
            $latest=Current $bounds $context.mode
            Guard $context $true
            if(!(Same $latest $expected) -or (Power) -ne $context.power -or $clock.ElapsedMilliseconds -gt 12000) {throw 'concurrent_change'}
            $attempted=$true;WriteValue $key $target.$key $bounds
            # Read-only convergence; never repeat a setter to force an ACK.
            $wait=[Diagnostics.Stopwatch]::StartNew();$confirmed=$false
            do {Guard $context $true;if((ReadValue $key $bounds[$key].valuePage) -eq $target.$key) {$confirmed=$true;break};Start-Sleep -Milliseconds 60}while($wait.ElapsedMilliseconds -lt 600)
            if(!$confirmed) {throw 'write_not_confirmed'}
            $expected[$key]=[long]$target.$key
        }
        if((BoundsStamp (Bounds)) -cne (BoundsStamp $bounds)) {throw 'final_state_changed'}
        Guard $context $true;$after=Current $bounds $context.mode
        Guard $context $true
        if(!(Same $after $target) -or (Power) -ne $context.power -or $clock.ElapsedMilliseconds -gt 12000) {throw 'final_state_changed'}
        $result.limitsWatts=$after;$result.changed=$order.Count -gt 0;return $result
    }catch {
        $code='operation_unknown';if($_.Exception.Message -match '^[a-z_]+$') {$code=$_.Exception.Message}
        $failure=@{ok=$false;available=$false;code=$code;changed=$false;recoveryRequired=$attempted}
        if($modeAttempted -and @(1,2,3,255) -contains $oldMode) {
            try {
                # Only our exact target state may be conditionally restored.
                # Zero/foreign/transition modes never authorize another setter.
                $ours=@{mode=$modeTarget;realMode=$modeTarget;power=$context.power};Guard $ours $true $true
                WriteMode $oldMode
                if(!(ConfirmMode $oldMode)) {throw 'mode_restore_unconfirmed'}
                Guard $context $true $true
                $failure.recoveryRequired=$false;$failure.rollbackConfirmed=$true
            }catch {$failure.recoveryRequired=$true}
        }
        return $failure
    }finally {if($script:locked) {$script:mutex.ReleaseMutex()};if($null -ne $script:mutex) {$script:mutex.Dispose()}}
}
'''


class LenovoOtherTdp:
    """No constructor hardware calls; no register/preset choice exposed to clients."""
    _owners_lock = threading.Lock()
    _owners = {}

    def __init__(self, read_identity, run_cmd, powershell_path, windows=None):
        self._identity, self._run_cmd, self._powershell = read_identity, run_cmd, powershell_path
        self._windows = os.name == "nt" if windows is None else windows
        self._lock, self._owner_id = threading.Lock(), object()
        self._last = self._owned = None
        self._uncertain = False

    def _send(self, request):
        identity = copy.deepcopy(self._identity())
        if not self._windows or not supports_identity(identity):
            return {"ok": False, "available": False, "code": "device_unsupported", "changed": False, "recoveryRequired": False}
        try:
            payload = base64.b64encode(json.dumps(request, separators=(",", ":")).encode()).decode("ascii")
            with tempfile.TemporaryDirectory(prefix="Playhub-Lenovo-Tdp-") as directory:
                file = Path(directory) / "tdp.ps1"
                file.write_text(_SCRIPT + "\nOperate | ConvertTo-Json -Compress -Depth 7\n", encoding="utf-8-sig")
                code, output, _ = self._run_cmd([self._powershell(), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(file), "-RequestBase64", payload], timeout=30)
            reply = json.loads(output.lstrip("\ufeff")) if code == 0 and len(output) <= 8192 else None
            if self._identity() == identity and self._valid(reply):
                return reply
        except (OSError, ValueError, TypeError, subprocess.SubprocessError):
            pass
        return {"ok": False, "available": False, "code": "operation_unknown", "changed": False, "recoveryRequired": request["action"] != "snapshot"}

    @staticmethod
    def _valid(value):
        return (isinstance(value, dict) and all(type(value.get(k)) is bool for k in ("ok", "available", "changed", "recoveryRequired"))
                and isinstance(value.get("code"), str) and (not value["ok"] or (value["available"] and not value["recoveryRequired"]
                    and isinstance(value.get("token"), str) and _TOKEN.fullmatch(value["token"])
                    and isinstance(value.get("deviceToken"), str) and _TOKEN.fullmatch(value["deviceToken"])
                    and type(value.get("tdpAvailable")) is bool
                    and ((value["tdpAvailable"] and valid_limits(value.get("limitsWatts")) and valid_bounds(value.get("boundsWatts")))
                         or (not value["tdpAvailable"] and value.get("limitsWatts") is None and value.get("boundsWatts") is None and value.get("writable") is False))
                    and type(value.get("writable")) is bool and type(value.get("mode")) is int and value["mode"] in (1, 2, 3, 224, 255)
                    and type(value.get("customModeAvailable")) is bool and value.get("nativeUnit") == 'W'
                    and type(value.get("modeEditable")) is bool
                    and type(value.get("modeControlAvailable")) is bool and value["modeControlAvailable"] == value["modeEditable"]
                    and type(value.get("realMode")) is int and value["realMode"] in (1, 2, 3, 224, 255)
                    and (type(value.get("externalPower")) is bool or value.get("externalPower") is None)
                    and (not value["customModeAvailable"] or (value["mode"] == value["realMode"] and value["mode"] in (1, 2, 3)
                         and type(value["externalPower"]) is bool))
                    and (not value["modeEditable"] or (value["mode"] == value["realMode"] and value["mode"] in (1, 2, 3, 255)
                         and type(value["externalPower"]) is bool))
                    and (not value["writable"] or (value["tdpAvailable"] and value["mode"] == value["realMode"] == 255 and type(value["externalPower"]) is bool
                         and fits(value["limitsWatts"], value["boundsWatts"]) and all(value["boundsWatts"][k]["capability"] & 7 == 7 for k in RAILS))))))

    def read_current(self):
        with self._lock:
            self._last = None
            value = self._send({"action": "snapshot"})
            relinquished = False
            if value.get("ok") is True:
                replacement = (self._owned is not None and (value["deviceToken"] != self._owned["deviceToken"]
                    or value["mode"] != 255 or value["realMode"] != 255 or (type(value["externalPower"]) is bool and value["externalPower"] != self._owned["externalPower"])
                    or (value["tdpAvailable"] and (value["token"] != self._owned["token"] or value["limitsWatts"] != self._owned["target"]))))
                if not self._uncertain and replacement:
                    with self._owners_lock:
                        old_device = self._owned["deviceToken"]
                        if self._owners.get(old_device) is self._owner_id:
                            del self._owners[old_device]
                    self._owned = None
                    relinquished = True
                self._last = copy.deepcopy(value)
            return {**value, "provider": "lenovo-other-tdp", "experimental": True, "hardwareValidated": False,
                    "readbackKind": "firmware-register", "restoreAvailable": bool(self._owned is not None and self._last
                        and self._last["writable"] and not self._uncertain), "baselineRelinquished": relinquished,
                    "manualRecoveryRequired": self._uncertain}

    def set_limits(self, limits, token, confirmed=False):
        if not valid_limits(limits):
            return {"ok": False, "code": "limits_invalid"}
        return self._change("set", token, confirmed, limits)

    def restore(self, token, confirmed=False):
        return self._change("restore", token, confirmed)

    def set_custom_mode(self, token, confirmed=False):
        return self.set_mode(255, token, confirmed)

    def set_mode(self, mode, token, confirmed=False):
        """Explicit documented preset choice; never adjusts a watt limit."""
        if type(mode) is not int or mode not in (1, 2, 3, 255):
            return {"ok": False, "code": "mode_forbidden"}
        if confirmed is not True or not isinstance(token, str):
            return {"ok": False, "code": "confirmation_required"}
        with self._lock:
            if self._uncertain:
                return {"ok": False, "code": "manual_recovery_required"}
            last = self._last
            if not last or last["token"] != token:
                return {"ok": False, "code": "fresh_read_required"}
            if not last["modeControlAvailable"]:
                return {"ok": False, "code": "mode_unavailable"}
            device_token = last["deviceToken"]
            with self._owners_lock:
                owner = self._owners.get(device_token)
                if owner is not None and owner is not self._owner_id:
                    return {"ok": False, "code": "owned_by_another_session"}
                self._owners[device_token] = self._owner_id
            result = self._send({"action": "mode", "mode": mode, "confirmed": True, "token": token, "expectedMode": last["mode"]})
            self._last = None
            self._uncertain = result.get("recoveryRequired") is True
            if result.get("ok") is True and (result["deviceToken"] != device_token or result["mode"] != mode or result["realMode"] != mode
                    or result["externalPower"] != last["externalPower"] or (last["mode"] != mode and result["changed"] is not True)):
                self._uncertain = True
                return {"ok": False, "code": "mode_readback_mismatch", "recoveryRequired": True}
            if result.get("ok") is True and mode != last["mode"]:
                self._owned = None
            if not self._uncertain and self._owned is None:
                with self._owners_lock:
                    if self._owners.get(device_token) is self._owner_id:
                        del self._owners[device_token]
            return result

    def _change(self, action, token, confirmed, limits=None):
        if confirmed is not True or not isinstance(token, str):
            return {"ok": False, "code": "confirmation_required"}
        with self._lock:
            if self._uncertain:
                return {"ok": False, "code": "manual_recovery_required"}
            last, owned = self._last, self._owned
            if not last or last["token"] != token:
                return {"ok": False, "code": "fresh_read_required"}
            if not last["writable"]:
                return {"ok": False, "code": "read_only_context"}
            if action == "restore" and owned is None:
                return {"ok": False, "code": "no_owned_change"}
            if owned and (owned["token"] != token or last["limitsWatts"] != owned["target"]):
                return {"ok": False, "code": "external_change_restore_skipped"}
            target = copy.deepcopy(owned["baseline"] if action == "restore" else limits)
            if not fits(target, last["boundsWatts"]):
                return {"ok": False, "code": "target_out_of_bounds"}
            with self._owners_lock:
                device_token = last["deviceToken"]
                owner = self._owners.get(device_token)
                if owner is not None and owner is not self._owner_id:
                    return {"ok": False, "code": "owned_by_another_session"}
                self._owners[device_token] = self._owner_id
            command = {"action": action, "confirmed": True, "token": token, "expected": copy.deepcopy(last["limitsWatts"]),
                       "baseline" if action == "restore" else "limits": target}
            result = self._send(command)
            self._last = None
            self._uncertain = result.get("recoveryRequired") is True
            if result.get("ok") is True:
                if (result["token"] != token or result["deviceToken"] != device_token or result["limitsWatts"] != target or not result["writable"]
                        or (target != last["limitsWatts"] and result["changed"] is not True)
                        or result["boundsWatts"] != last["boundsWatts"] or result["externalPower"] != last["externalPower"]):
                    self._uncertain = True
                    return {"ok": False, "code": "readback_mismatch", "recoveryRequired": True}
                if action == "set" and target != last["limitsWatts"]:
                    self._owned = {"token": token, "deviceToken": device_token,
                                   "externalPower": last["externalPower"],
                                   "baseline": copy.deepcopy(owned["baseline"] if owned else last["limitsWatts"]), "target": target}
                elif action == "restore":
                    self._owned = None
            if not self._uncertain and self._owned is None:
                with self._owners_lock:
                    if self._owners.get(device_token) is self._owner_id:
                        del self._owners[device_token]
            return result
