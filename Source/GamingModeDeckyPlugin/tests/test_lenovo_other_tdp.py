"""Actual Python and PowerShell logic with inert CIM/identity/power fakes only."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("lenovo_other", ROOT / "quick_settings/lenovo_other_tdp.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
count = 0


def check(value):
    global count
    assert value
    count += 1


for model, versions in m.IDENTITIES.items():
    for version in versions:
        check(m.supports_identity({"manufacturer": "LENOVO", "model": model, "version": version}))
        check(m.supports_identity({"manufacturer": "LENOVO", "model": model, "version": "Lenovo " + version}))
for record in ({"manufacturer": "LENOVO", "model": "83N0", "version": "Legion Go 8AHP2"},
               {"manufacturer": "LENOVO", "model": "83E1", "version": "Legion Go 8APU1"},
               {"manufacturer": "LENOVO", "model": "83N0extra", "version": "Legion Go 8ASP2"},
               {"manufacturer": "OTHER", "model": "83N0", "version": "Legion Go 8ASP2"}):
    check(not m.supports_identity(record))

bounds = {"spl": dict(minimum=5, maximum=31, step=2, default=15, capability=7, capabilityId=0x0102ff00, valuePage=255),
          "sppt": dict(minimum=8, maximum=44, step=4, default=20, capability=7, capabilityId=0x0101ff00, valuePage=255),
          "fppt": dict(minimum=10, maximum=58, step=6, default=22, capability=7, capabilityId=0x0103ff00, valuePage=255)}
check(m.fits({"spl": 17, "sppt": 24, "fppt": 34}, bounds))
check(not m.fits({"spl": 17, "sppt": 22, "fppt": 34}, bounds))
check(not m.fits({"spl": 17, "sppt": 24, "fppt": 64}, bounds))
check(not m.valid_limits({"spl": True, "sppt": 20, "fppt": 28}))
foreign_bounds = copy.deepcopy(bounds)
foreign_bounds["spl"]["capabilityId"] = 0x0202ff00
check(not m.valid_bounds(foreign_bounds))


class Fake(m.LenovoOtherTdp):
    def __init__(self, device):
        super().__init__(lambda: dict(manufacturer="LENOVO", model="83N0", version="Legion Go 8ASP2"), None, None, True)
        self.device, self.calls = device, []

    def _send(self, request):
        self.calls.append(copy.deepcopy(request))
        d = self.device
        reply = {"ok": True, "available": True, "code": "", "changed": False, "recoveryRequired": False,
                 "token": d["token"], "deviceToken": d["deviceToken"], "tdpAvailable": not d.get("missingCaps", False), "limitsWatts": copy.deepcopy(d["limits"]),
                 "boundsWatts": copy.deepcopy(bounds), "mode": d.get("mode", 255), "realMode": d.get("mode", 255),
                 "externalPower": True, "writable": d.get("mode", 255) == 255,
                 "customModeAvailable": d.get("mode", 255) in (1, 2, 3), "modeEditable": d.get("mode", 255) in (1, 2, 3, 255),
                 "modeControlAvailable": d.get("mode", 255) in (1, 2, 3, 255), "nativeUnit": "W"}
        if d.get("missingCaps"):
            reply.update(limitsWatts=None, boundsWatts=None, writable=False)
        if request["action"] != "snapshot":
            if d.get("failure"):
                return {"ok": False, "available": False, "code": "injected", "changed": False, "recoveryRequired": True}
            if request["action"] in ("custom", "mode"):
                target_mode = request.get("mode", 255)
                changed = d.get("mode", 255) != target_mode
                d["mode"] = target_mode
                reply.update(mode=target_mode, realMode=target_mode, writable=target_mode == 255 and reply["tdpAvailable"],
                             customModeAvailable=target_mode in (1, 2, 3), changed=changed)
                return reply
            target = request["baseline"] if request["action"] == "restore" else request["limits"]
            reply["changed"] = target != d["limits"]
            d["limits"] = copy.deepcopy(target)
            reply["limitsWatts"] = copy.deepcopy(target)
        return reply


d = dict(token="a" * 64, deviceToken="b" * 64, limits={"spl": 15, "sppt": 20, "fppt": 28})
a, b = Fake(d), Fake(d)
check(a.read_current()["writable"])
check(a.restore(d["token"], True)["code"] == "no_owned_change")
check(a.set_limits({"spl": 17, "sppt": 24, "fppt": 34}, d["token"], True)["ok"])
b.read_current()
check(b.set_limits({"spl": 15, "sppt": 20, "fppt": 28}, d["token"], True)["code"] == "owned_by_another_session")
a.read_current()
check(a.restore(d["token"], True)["limitsWatts"] == {"spl": 15, "sppt": 20, "fppt": 28})
b.read_current()
check(b.set_limits({"spl": 17, "sppt": 24, "fppt": 34}, d["token"], True)["ok"])
d["limits"]["spl"] = 19
d["token"] = "c" * 64
c = Fake(d)
c.read_current()
check(c.set_limits({"spl": 17, "sppt": 24, "fppt": 34}, d["token"], True)["code"] == "owned_by_another_session")
released = b.read_current()
check(released["baselineRelinquished"] and not released["restoreAvailable"] and not released["manualRecoveryRequired"])
check(b.restore(d["token"], True)["code"] == "no_owned_change")
check(b.set_limits({"spl": 17, "sppt": 24, "fppt": 34}, d["token"], True)["ok"])
d2 = dict(token="d" * 64, deviceToken="e" * 64, limits={"spl": 15, "sppt": 20, "fppt": 28}, failure=True)
x, y = Fake(d2), Fake(d2)
x.read_current()
check(x.set_limits({"spl": 17, "sppt": 24, "fppt": 34}, d2["token"], True)["recoveryRequired"])
unknown_read = x.read_current()
check(unknown_read["manualRecoveryRequired"] and not unknown_read["restoreAvailable"] and not unknown_read["baselineRelinquished"])
y.read_current()
check(y.set_limits({"spl": 17, "sppt": 24, "fppt": 34}, d2["token"], True)["code"] == "owned_by_another_session")
d3 = dict(token="f" * 64, deviceToken="1" * 64, limits={"spl": 15, "sppt": 20, "fppt": 28}, mode=2)
z = Fake(d3)
check(z.read_current()["mode"] == 2)
check(z.set_limits({"spl": 17, "sppt": 24, "fppt": 34}, d3["token"], True)["code"] == "read_only_context")
check(z.calls[-1]["action"] == "snapshot")
check(z.set_custom_mode(d3["token"], False)["code"] == "confirmation_required")
check(z.set_custom_mode(d3["token"], True)["mode"] == 255)
check(z.calls[-1] == {"action": "mode", "mode": 255, "confirmed": True, "token": d3["token"], "expectedMode": 2})
d4 = dict(token="2" * 64, deviceToken="3" * 64, limits={"spl": 15, "sppt": 20, "fppt": 28})
owned_mode = Fake(d4)
owned_mode.read_current()
owned_mode.set_limits({"spl": 17, "sppt": 24, "fppt": 34}, d4["token"], True)
d4.update(mode=2, token="4" * 64)
relinquished_mode = owned_mode.read_current()
check(relinquished_mode["baselineRelinquished"] and not relinquished_mode["manualRecoveryRequired"])
check(relinquished_mode["restoreAvailable"] is False and relinquished_mode["customModeAvailable"])
check(owned_mode.set_custom_mode(d4["token"], True)["ok"])
owned_mode.read_current()
owned_mode.set_limits({"spl": 19, "sppt": 24, "fppt": 34}, d4["token"], True)
owned_mode.read_current()
check(owned_mode.set_mode(1, d4["token"], True)["mode"] == 1)
quiet = owned_mode.read_current()
check(not quiet["restoreAvailable"] and not quiet["manualRecoveryRequired"] and quiet["customModeAvailable"])
check(owned_mode.set_mode(224, d4["token"], True)["code"] == "mode_forbidden")
d5 = dict(token="5" * 64, deviceToken="6" * 64, limits={"spl": 15, "sppt": 20, "fppt": 28})
missing_caps_owner = Fake(d5)
missing_caps_owner.read_current()
missing_caps_owner.set_limits({"spl": 17, "sppt": 24, "fppt": 34}, d5["token"], True)
d5["missingCaps"] = True
mode_only = missing_caps_owner.read_current()
check(mode_only["modeControlAvailable"] and not mode_only["tdpAvailable"] and not mode_only["restoreAvailable"] and not mode_only["baselineRelinquished"])
check(m.LenovoOtherTdp._valid(mode_only))
check(missing_caps_owner.set_mode(2, d5["token"], True)["mode"] == 2)

PS = r'''
$script:checks=0
function Check([bool]$value,[string]$label) {if(!$value) {throw "check_failed:$label"};$script:checks++}
function Instance([string]$name,[string]$key) {return [pscustomobject]@{Active=$true;InstanceName=$key;CimSystemProperties=[pscustomobject]@{ClassName=$name;Namespace='root/wmi';ServerName='local'}}}
function ResetFake {
    $script:values=@{spl=[long]15;sppt=[long]20;fppt=[long]28};$script:writes=@();$script:modeWrites=@();$script:mode=255;$script:realMode=255;$script:power=1;$script:foreign=$false
    $script:throwWrite=$false;$script:ignoreWrite=$false;$script:flipPower=$false;$script:replaceOther=$false;$script:nonePage=$false
    $script:modeThrow=$false;$script:modeForeign=$false;$script:modeIgnore=$false
    $script:modeThrowTarget=-1
    $script:badModeType=$false
    $script:capReads=0;$script:changeAtCap=0
    $script:tdpSchema=$true;$script:missingCaps=$false
    $script:featureReads=0;$script:changeModeAtRead=0;$script:replaceAtRead=0;$script:identityAtRead=0
    $script:identity='exact-device-bios';$script:deviceReadIdentity='exact-device-bios';$script:providerHash='provider-hash';$script:rows=@();$script:request=[pscustomobject]@{action='snapshot'}
    $script:other=Instance 'LENOVO_OTHER_METHOD' 'other0';$script:gamezone=Instance 'LENOVO_GAMEZONE_DATA' 'gamezone0'
    foreach($rail in @('spl','sppt','fppt')) {
        $min=@{spl=5;sppt=8;fppt=10}[$rail];$max=@{spl=31;sppt=44;fppt=58}[$rail];$step=@{spl=2;sppt=4;fppt=6}[$rail];$def=@{spl=15;sppt=20;fppt=22}[$rail]
        $script:rows+=,[pscustomobject]@{Active=$true;InstanceName=('caps-'+$rail);CimSystemProperties=[pscustomobject]@{ClassName='LENOVO_CAPABILITY_DATA_01';Namespace='root/wmi'};
            IDs=[uint32](FeatureId $rail 255);Capability=[uint32]7;DefaultValue=[uint32]$def;Step=[uint32]$step;MinValue=[uint32]$min;MaxValue=[uint32]$max}
    }
}
function OpenProvider {}
function DeviceIdentity {return $script:deviceReadIdentity}
function Power {return $script:power}
function Competitor {return $script:foreign}
function Get-CimInstance($Namespace,$ClassName,$OperationTimeoutSec) {
    if($ClassName -ceq 'LENOVO_CAPABILITY_DATA_01') {if($script:missingCaps) {throw 'capability_missing'};$script:capReads++;if($script:changeAtCap -eq $script:capReads) {$script:values.spl=[long]19};return $script:rows}
    if($ClassName -ceq 'LENOVO_GAMEZONE_DATA') {return $script:gamezone}
    if($ClassName -ceq 'LENOVO_OTHER_METHOD') {if($script:replaceOther) {return Instance 'LENOVO_OTHER_METHOD' 'replacement'};return $script:other}
    throw 'unexpected_native_query'
}
function Invoke-CimMethod($InputObject,$MethodName,$Arguments,$OperationTimeoutSec) {
    if($MethodName -ceq 'IsSupportSmartFan') {return @{Data=[uint32]6}}
    if($MethodName -ceq 'GetSmartFanMode') {if($script:badModeType) {return @{Data='255'}};return @{Data=[uint32]$script:mode}}
    if($MethodName -ceq 'GetThermalMode') {return @{Data=[uint32]$script:realMode}}
    if($MethodName -ceq 'SetSmartFanMode') {
        $script:modeWrites+=,[int]$Arguments.Data
        if(!$script:modeIgnore) {$script:mode=[int]$Arguments.Data;$script:realMode=$script:mode}
        if($script:modeForeign -and $Arguments.Data -eq 255) {$script:mode=3;$script:realMode=3}
        if(($script:modeThrow -and $Arguments.Data -eq 255) -or $Arguments.Data -eq $script:modeThrowTarget) {throw 'mode_throw_after_apply'}
        return $null
    }
    $feature=([uint32]$Arguments.IDs -shr 16) -band 255;$page=([uint32]$Arguments.IDs -shr 8) -band 255
    $rail=@{1='sppt';2='spl';3='fppt'}[[int]$feature]
    if(!$rail -or ([uint32]$Arguments.IDs -shr 24) -ne 1 -or ([uint32]$Arguments.IDs -band 255) -ne 0) {throw 'foreign_feature'}
    if($script:nonePage -and $page -ne 0) {throw 'page_unavailable'}
    if($MethodName -ceq 'GetFeatureValue') {
        $script:featureReads++
        if($script:changeModeAtRead -eq $script:featureReads) {$script:mode=3;$script:realMode=3}
        if($script:replaceAtRead -eq $script:featureReads) {$script:replaceOther=$true}
        if($script:identityAtRead -eq $script:featureReads) {$script:deviceReadIdentity='replacement-device'}
        return @{value=[uint32]$script:values[$rail]}
    }
    if($MethodName -cne 'SetFeatureValue') {throw 'foreign_method'}
    $script:writes+=,$rail
    if(!$script:ignoreWrite) {$script:values[$rail]=[long]$Arguments.value}
    if($script:flipPower) {$script:power=0}
    if($script:throwWrite) {throw 'injected_after_write'}
    return $null
}
function SetRequest($snapshot,[long]$spl=17,[long]$sppt=24,[long]$fppt=34) {
    $script:request=[pscustomobject]@{action='set';confirmed=$true;token=$snapshot.token;
        expected=[pscustomobject]@{spl=$snapshot.limitsWatts.spl;sppt=$snapshot.limitsWatts.sppt;fppt=$snapshot.limitsWatts.fppt};
        limits=[pscustomobject]@{spl=$spl;sppt=$sppt;fppt=$fppt}}
}
ResetFake;$r=Operate
Check ($r.ok -and $r.writable -and $r.boundsWatts.spl.maximum -eq 31 -and $r.boundsWatts.sppt.step -eq 4 -and $r.boundsWatts.fppt.maximum -eq 58) 'actual-different-rail-bounds'
Check ($script:writes.Count -eq 0) 'read-only-snapshot'
SetRequest $r;$r=Operate
Check ($r.ok -and ($script:writes -join ',') -ceq 'fppt,sppt,spl' -and $r.limitsWatts.spl -eq 17) 'increase-order-readback'
SetRequest $r 15 20 28;$r=Operate
Check ($r.ok -and ($script:writes[3..5] -join ',') -ceq 'spl,sppt,fppt') 'decrease-order-readback'
ResetFake;$script:mode=2;$script:realMode=2;$r=Operate
Check ($r.ok -and !$r.writable -and $r.mode -eq 2 -and $script:writes.Count -eq 0) 'noncustom-useful-report'
SetRequest $r;$r=Operate;Check (!$r.ok -and !$r.recoveryRequired -and $script:writes.Count -eq 0 -and $script:mode -eq 2) 'never-auto-enter-custom'
ResetFake;$script:foreign=$true;$r=Operate;Check ($r.ok -and !$r.writable) 'oem-active-read-still-available'
ResetFake;$script:power=-1;$r=Operate;Check ($r.ok -and !$r.writable -and $null -eq $r.externalPower) 'unknown-power-readonly'
ResetFake;$r=Operate;SetRequest $r 17 22 34;$r=Operate;Check (!$r.ok -and $script:writes.Count -eq 0) 'real-step-veto'
ResetFake;$r=Operate;SetRequest $r 17 24 64;$r=Operate;Check (!$r.ok -and $script:writes.Count -eq 0) 'real-max-veto'
ResetFake;$r=Operate;SetRequest $r;$request.token='foreign';$r=Operate;Check (!$r.ok -and $script:writes.Count -eq 0) 'token-mismatch'
ResetFake;$r=Operate;SetRequest $r;$script:throwWrite=$true;$r=Operate
Check (!$r.ok -and $r.recoveryRequired -and $script:writes.Count -eq 1 -and $script:values.fppt -eq 34) 'throw-after-write-no-rollback'
ResetFake;$r=Operate;SetRequest $r;$script:ignoreWrite=$true;$r=Operate
Check (!$r.ok -and $r.recoveryRequired -and $script:writes.Count -eq 1) 'ignored-write-no-setter-retry'
ResetFake;$r=Operate;SetRequest $r;$script:flipPower=$true;$r=Operate
Check (!$r.ok -and $r.recoveryRequired -and $script:writes.Count -eq 1) 'power-change-after-write'
ResetFake;$r=Operate;SetRequest $r;$script:changeAtCap=3;$r=Operate
Check (!$r.ok -and !$r.recoveryRequired -and $script:writes.Count -eq 0 -and $script:values.spl -eq 19) 'last-capability-getter-foreign-before-write'
ResetFake;$r=Operate;SetRequest $r;$script:changeAtCap=6;$r=Operate
Check (!$r.ok -and $r.recoveryRequired -and $script:writes.Count -eq 3 -and $script:values.spl -eq 19) 'last-capability-getter-foreign-after-write'
ResetFake;$r=Operate;SetRequest $r;$script:rows[0].MaxValue=[uint32]29;$r=Operate
Check (!$r.ok -and !$r.recoveryRequired -and $script:writes.Count -eq 0) 'firmware-bounds-change-invalidates-token'
ResetFake;$script:rows+=,$script:rows[0];$r=Operate;Check ($r.ok -and !$r.tdpAvailable -and !$r.writable -and $script:writes.Count -eq 0) 'duplicate-capability'
ResetFake;$script:rows[0].Step=[uint32]0;$r=Operate;Check ($r.ok -and !$r.tdpAvailable -and !$r.writable) 'zero-step-unavailable'
ResetFake;$fallback=$script:rows[0].PSObject.Copy();$fallback.IDs=[uint32](FeatureId 'spl' 0);$script:rows+=,$fallback;$script:rows[0].Step=[uint32]0;$r=Operate
Check ($r.ok -and !$r.tdpAvailable -and !$r.writable) 'malformed-present-page-is-not-absence'
ResetFake;$script:realMode=3;$r=Operate;Check (!$r.ok -and $script:writes.Count -eq 0) 'selected-real-mode-transition-unknown'
ResetFake;$script:badModeType=$true;$r=Operate;Check (!$r.ok -and $r.code -ceq 'mode_unknown' -and $script:writes.Count -eq 0) 'mode-type-not-coerced'
ResetFake;foreach($row in $script:rows) {$row.Capability=[uint32]3};$r=Operate;Check ($r.ok -and !$r.writable) 'get-without-set-capability'
ResetFake;$script:nonePage=$true;$r=Operate;Check ($r.ok -and $r.boundsWatts.spl.valuePage -eq 0) 'documented-none-value-page'
ResetFake;$script:replaceOther=$true;$r=Operate;Check ($r.ok -and !$r.tdpAvailable -and !$r.writable -and $r.modeControlAvailable -and $script:writes.Count -eq 0) 'replaced-other-instance-never-grants-tdp'
ResetFake;$script:changeModeAtRead=6;$r=Operate;Check (!$r.ok -and $script:writes.Count -eq 0) 'final-current-getter-mode-drift'
ResetFake;$script:replaceAtRead=6;$r=Operate;Check ($r.ok -and !$r.tdpAvailable -and !$r.writable -and $script:writes.Count -eq 0) 'final-current-getter-other-instance-drift'
ResetFake;$script:identityAtRead=6;$r=Operate;Check (!$r.ok -and $script:writes.Count -eq 0) 'final-current-getter-device-drift'
ResetFake;$script:mode=2;$script:realMode=2;$r=Operate
$script:request=[pscustomobject]@{action='custom';confirmed=$true;token=$r.token;expectedMode=2};$r=Operate
Check ($r.ok -and $r.mode -eq 255 -and $r.realMode -eq 255 -and $r.changed -and ($script:modeWrites -join ',') -ceq '255' -and $script:writes.Count -eq 0) 'explicit-custom-no-tdp-write'
ResetFake;$script:mode=2;$script:realMode=2;$r=Operate
$script:request=[pscustomobject]@{action='custom';confirmed=$true;token=$r.token;expectedMode=2};$script:modeThrow=$true;$r=Operate
Check (!$r.ok -and !$r.recoveryRequired -and $r.rollbackConfirmed -and ($script:modeWrites -join ',') -ceq '255,2') 'custom-failure-conditional-own-rollback'
ResetFake;$script:mode=2;$script:realMode=2;$r=Operate
$script:request=[pscustomobject]@{action='custom';confirmed=$true;token=$r.token;expectedMode=2};$script:modeForeign=$true;$r=Operate
Check (!$r.ok -and $r.recoveryRequired -and ($script:modeWrites -join ',') -ceq '255' -and $script:mode -eq 3) 'custom-foreign-mode-never-rolled-back'
ResetFake;$script:mode=2;$script:realMode=2;$r=Operate
$script:request=[pscustomobject]@{action='custom';confirmed=$true;token=$r.token;expectedMode=2};$script:foreign=$true;$r=Operate
Check (!$r.ok -and !$r.recoveryRequired -and $script:modeWrites.Count -eq 0) 'custom-oem-veto'
ResetFake;$r=Operate
$script:request=[pscustomobject]@{action='mode';mode=1;confirmed=$true;token=$r.token;expectedMode=255};$r=Operate
Check ($r.ok -and $r.mode -eq 1 -and $r.realMode -eq 1 -and !$r.writable -and $r.customModeAvailable -and $r.modeEditable -and ($script:modeWrites -join ',') -ceq '1') 'explicit-quiet-leaves-custom'
ResetFake;$r=Operate
$script:request=[pscustomobject]@{action='mode';mode=1;confirmed=$true;token=$r.token;expectedMode=255};$script:modeThrowTarget=1;$r=Operate
Check (!$r.ok -and !$r.recoveryRequired -and $r.rollbackConfirmed -and ($script:modeWrites -join ',') -ceq '1,255') 'failed-preset-restores-owned-custom'
ResetFake;$r=Operate
$script:request=[pscustomobject]@{action='mode';mode=224;confirmed=$true;token=$r.token;expectedMode=255};$r=Operate
Check (!$r.ok -and !$r.recoveryRequired -and $script:modeWrites.Count -eq 0) 'no-extreme-write'
ResetFake;$script:missingCaps=$true;$r=Operate
Check ($r.ok -and $r.modeControlAvailable -and !$r.tdpAvailable -and $null -eq $r.boundsWatts -and $null -eq $r.limitsWatts -and !$r.writable) 'mode-only-missing-caps-honest-null-data'
$script:request=[pscustomobject]@{action='mode';mode=2;confirmed=$true;token=$r.token;expectedMode=255};$r=Operate
Check ($r.ok -and $r.mode -eq 2 -and $r.realMode -eq 2 -and !$r.tdpAvailable -and ($script:modeWrites -join ',') -ceq '2') 'leave-custom-even-without-rails'
ResetFake;$script:tdpSchema=$false;$script:other=$null;$r=Operate
Check ($r.ok -and $r.modeControlAvailable -and !$r.tdpAvailable -and $null -eq $r.boundsWatts) 'mode-only-missing-other-schema'
$bad=$false;try {FeatureId 'gpu' 255}catch{$bad=$true};Check $bad 'no-arbitrary-feature'
$bad=$false;try {FeatureId 'spl' 4}catch{$bad=$true};Check $bad 'no-arbitrary-mode-page'
Add-Type -TypeDefinition 'using System.Collections;public class InertLenovoProperties:IEnumerable {public Hashtable Data=new Hashtable();public object this[string key]{get{return Data[key];}}public IEnumerator GetEnumerator(){return Data.Values.GetEnumerator();}}'
function FakeMethod($id,$incoming,$outgoing) {
    $params=@{};foreach($name in $incoming) {$params[$name]=[pscustomobject]@{CimType='UInt32';Qualifiers=@{In=@{Value=$true}}}}
    foreach($name in $outgoing) {$params[$name]=[pscustomobject]@{CimType='UInt32';Qualifiers=@{Out=@{Value=$true}}}}
    return [pscustomobject]@{Parameters=$params;Qualifiers=@{Implemented=@{Value=$true};WmiMethodId=@{Value=$id}}}
}
function FakeClass($name,$guid) {
    $props=[InertLenovoProperties]::new();$props.Data.Add('InstanceName',[pscustomobject]@{CimType='String';Qualifiers=@{Key=@{Value=$true}}})
    $props.Data.Add('Active',[pscustomobject]@{CimType='Boolean';Qualifiers=@{}})
    return [pscustomobject]@{CimClassName=$name;CimClassQualifiers=@{Dynamic=@{Value=$true};Provider=@{Value='WmiProv'};guid=@{Value=$guid}};CimClassProperties=$props;CimClassMethods=@{}}
}
$otherClass=FakeClass 'LENOVO_OTHER_METHOD' 'DC2A8805-3A8C-41BA-A6F7-092E0089CD3B'
$otherClass.CimClassMethods.GetFeatureValue=FakeMethod 17 @('IDs') @('value');$otherClass.CimClassMethods.SetFeatureValue=FakeMethod 18 @('IDs','value') @()
$modeClass=FakeClass 'LENOVO_GAMEZONE_DATA' '887B54E3-DDDC-4B2C-8B88-68A26A8835D0'
$modeClass.CimClassMethods.IsSupportSmartFan=FakeMethod 43 @() @('Data');$modeClass.CimClassMethods.SetSmartFanMode=FakeMethod 44 @('Data') @()
$modeClass.CimClassMethods.GetSmartFanMode=FakeMethod 45 @() @('Data');$modeClass.CimClassMethods.GetThermalMode=FakeMethod 55 @() @('Data')
$capsClass=FakeClass 'LENOVO_CAPABILITY_DATA_01' '7A8F5407-CB67-4D6E-B547-39B3BE018154'
foreach($name in @('IDs','Capability','DefaultValue','Step','MinValue','MaxValue')) {$capsClass.CimClassProperties.Data.Add($name,[pscustomobject]@{CimType='UInt32';Qualifiers=@{}})}
Check (ClassesValid $otherClass $capsClass $modeClass) 'whole-schema-positive'
Check (ModeSchema $modeClass) 'mode-schema-independent-of-caps'
$capsClass.CimClassProperties['Step'].CimType='UInt64';Check (!(ClassesValid $otherClass $capsClass $modeClass)) 'schema-bound-type-mismatch'
$capsClass.CimClassProperties['Step'].CimType='UInt32';$otherClass.CimClassMethods.SetFeatureValue.Qualifiers.WmiMethodId.Value=99
Check (!(ClassesValid $otherClass $capsClass $modeClass)) 'schema-method-id-mismatch'
@{ok=$true;checks=$script:checks;nativeCalls=0}|ConvertTo-Json -Compress
'''
with tempfile.TemporaryDirectory(prefix="Playhub-Lenovo-Inert-") as temp:
    script = Path(temp) / "checks.ps1"
    script.write_text(m._SCRIPT + PS, encoding="utf-8-sig")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-File", str(script), "-RequestBase64", "e30="],
                            capture_output=True, text=True, timeout=25)
    if result.returncode:
        raise AssertionError(result.stdout + result.stderr)
    ps = json.loads(result.stdout.strip().lstrip("\ufeff"))
    assert ps["ok"] and ps["nativeCalls"] == 0
print(json.dumps({"ok": True, "pythonChecks": count, "actualPowerShellChecks": ps["checks"], "nativeCalls": 0}))
