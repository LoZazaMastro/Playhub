import base64
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("actual_tdp",ROOT/"quick_settings/asus_handheld_tdp.py")
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
TOKEN="a"*64
MODEL={"manufacturer":"ASUSTeK COMPUTER INC.","model":"ROG Xbox Ally X RC73XA_RC73XA"}
BASE={"spl":25,"sppt":30,"fppt":35}

def report(**extra):
    return {"ok":True,"available":True,"code":"","provider":"asus-rc73xa-tdp","token":TOKEN,
            "limitsWatts":dict(BASE),"minimumWatts":7,"maximumWatts":35,"externalPower":True,
            "changed":False,"recoveryRequired":False,"readbackKind":"firmware-register","hardwareValidated":False,
            "oemControllerActive":False,"writeAvailable":True,**extra}

class Boundary(unittest.TestCase):
    def setUp(self):
        self.identity=dict(MODEL);self.calls=[];self.response=report()
        def run(args,timeout):
            self.calls.append(json.loads(base64.b64decode(args[-1])))
            self.assertEqual(timeout,24)
            self.assertTrue(Path(args[6]).is_file())
            return 0,json.dumps(self.response),""
        self.native=m.AsusHandheldTdp(lambda:self.identity,run,lambda:"unexecuted-powershell.exe",windows=True)
    def test_desktop_other_ally_spoof_and_unknown_never_invoke(self):
        for v in ({"manufacturer":"ASUS","model":"RC71L"},{"manufacturer":"LENOVO","model":"83E1"},
                  {"manufacturer":"Other","model":"RC73XA"},{"manufacturer":"ASUS","model":"RC73XA-2"},{},None):
            self.identity=v
            self.assertFalse(self.native.get()["available"])
        self.assertEqual(self.calls,[])
    def test_confirm_and_typed_target_are_required(self):
        self.native.get();before=len(self.calls)
        for v in [None,{},dict(token=TOKEN,watts=True,confirmed=True),dict(token=TOKEN,watts=17,confirmed="true"),
                  dict(token=TOKEN,watts=6,confirmed=True),dict(token="b"*64,watts=17,confirmed=True)]:
            self.assertFalse(self.native.apply(v)["ok"])
        self.assertEqual(len(self.calls),before)
    def test_apply_and_restore_use_only_server_owned_baseline(self):
        self.native.get();self.response=report(limitsWatts=dict.fromkeys(m.KEYS,17),changed=True)
        self.assertTrue(self.native.apply(dict(token=TOKEN,watts=17,confirmed=True,register=42,baseline=dict.fromkeys(m.KEYS,255)))["ok"])
        self.assertEqual(self.calls[-1]["expected"],BASE)
        self.assertNotIn("baseline",self.calls[-1])
        self.assertEqual(self.native.apply(dict(token=TOKEN,watts=18,confirmed=True))["code"],"fresh_read_required")
        self.response=report(changed=True)
        self.assertTrue(self.native.restore(dict(token=TOKEN,confirmed=True,baseline=dict.fromkeys(m.KEYS,255)))["ok"])
        self.assertEqual(self.calls[-1]["baseline"],BASE)
    def test_successive_updates_keep_the_original_baseline(self):
        self.native.get()
        self.response=report(limitsWatts=dict.fromkeys(m.KEYS,20),changed=True)
        self.assertTrue(self.native.apply(dict(token=TOKEN,watts=20,confirmed=True))["ok"])
        self.native.get()
        self.response=report(limitsWatts=dict.fromkeys(m.KEYS,21),changed=True)
        self.assertTrue(self.native.apply(dict(token=TOKEN,watts=21,confirmed=True))["ok"])
        self.assertEqual(self.calls[-1]["expected"],dict.fromkeys(m.KEYS,20))
        self.native.get()
        self.response=report(changed=True)
        self.assertTrue(self.native.restore(dict(token=TOKEN,confirmed=True))["ok"])
        self.assertEqual(self.calls[-1]["baseline"],BASE)
        self.assertEqual(self.calls[-1]["expected"],dict.fromkeys(m.KEYS,21))
    def test_failed_second_update_preserves_original_owner_after_confirmed_rollback(self):
        self.native.get();self.response=report(limitsWatts=dict.fromkeys(m.KEYS,20),changed=True)
        self.assertTrue(self.native.apply(dict(token=TOKEN,watts=20,confirmed=True))["ok"])
        self.native.get()
        self.response=report(ok=False,code="write_rejected",limitsWatts=dict.fromkeys(m.KEYS,20),rollbackConfirmed=True)
        self.assertFalse(self.native.apply(dict(token=TOKEN,watts=21,confirmed=True))["ok"])
        self.response=report(changed=True)
        self.assertTrue(self.native.restore(dict(token=TOKEN,confirmed=True))["ok"])
        self.assertEqual(self.calls[-1]["baseline"],BASE)
        self.assertEqual(self.calls[-1]["expected"],dict.fromkeys(m.KEYS,20))
    def test_fresh_foreign_state_relinquishes_without_writing_then_uses_a_new_baseline(self):
        self.native.get();self.response=report(limitsWatts=dict.fromkeys(m.KEYS,20),changed=True)
        self.assertTrue(self.native.apply(dict(token=TOKEN,watts=20,confirmed=True))["ok"])
        count=len(self.calls)
        self.response=report(limitsWatts=dict.fromkeys(m.KEYS,19));value=self.native.get()
        self.assertTrue(value["ownedRelinquished"])
        self.assertFalse(value["restoreAvailable"])
        self.assertFalse(value["manualRecoveryRequired"])
        self.assertEqual(self.calls[count:],[{"action":"get"}])
        self.response=report(limitsWatts=dict.fromkeys(m.KEYS,21),changed=True)
        self.assertTrue(self.native.apply(dict(token=TOKEN,watts=21,confirmed=True))["ok"])
        self.native.get();self.response=report(limitsWatts=dict.fromkeys(m.KEYS,19),changed=True)
        self.assertTrue(self.native.restore(dict(token=TOKEN,confirmed=True))["ok"])
        self.assertEqual(self.calls[-1]["baseline"],dict.fromkeys(m.KEYS,19))
    def test_ac_to_dc_reanchors_without_restoring_the_old_ac_baseline(self):
        self.native.get();self.response=report(limitsWatts=dict.fromkeys(m.KEYS,20),changed=True)
        self.assertTrue(self.native.apply(dict(token=TOKEN,watts=20,confirmed=True))["ok"])
        self.response=report(limitsWatts=dict.fromkeys(m.KEYS,20),externalPower=False,maximumWatts=25)
        self.assertTrue(self.native.get()["ownedRelinquished"])
        self.response=report(limitsWatts=dict.fromkeys(m.KEYS,21),externalPower=False,maximumWatts=25,changed=True)
        self.assertTrue(self.native.apply(dict(token=TOKEN,watts=21,confirmed=True))["ok"])
        self.native.get();self.response=report(limitsWatts=dict.fromkeys(m.KEYS,20),externalPower=False,maximumWatts=25,changed=True)
        self.assertTrue(self.native.restore(dict(token=TOKEN,confirmed=True))["ok"])
        self.assertEqual(self.calls[-1]["baseline"],dict.fromkeys(m.KEYS,20))
        self.assertIs(self.calls[-1]["externalPower"],False)
    def test_dc_to_ac_oem_preset_change_uses_the_fresh_ac_baseline(self):
        dc=dict(spl=18,sppt=20,fppt=25)
        self.response=report(limitsWatts=dc,externalPower=False,maximumWatts=25);self.native.get()
        self.response=report(limitsWatts=dict.fromkeys(m.KEYS,17),externalPower=False,maximumWatts=25,changed=True)
        self.assertTrue(self.native.apply(dict(token=TOKEN,watts=17,confirmed=True))["ok"])
        self.response=report();self.assertTrue(self.native.get()["ownedRelinquished"])
        self.response=report(limitsWatts=dict.fromkeys(m.KEYS,30),changed=True)
        self.assertTrue(self.native.apply(dict(token=TOKEN,watts=30,confirmed=True))["ok"])
        self.native.get();self.response=report(changed=True)
        self.assertTrue(self.native.restore(dict(token=TOKEN,confirmed=True))["ok"])
        self.assertEqual(self.calls[-1]["baseline"],BASE)
    def test_write_uncertainty_is_not_cleared_by_a_new_power_source_or_oem_value(self):
        self.native.get();self.response=report(limitsWatts=dict.fromkeys(m.KEYS,18),changed=True)
        self.assertFalse(self.native.apply(dict(token=TOKEN,watts=17,confirmed=True))["ok"])
        self.response=report(limitsWatts=dict.fromkeys(m.KEYS,20),externalPower=False,maximumWatts=25)
        value=self.native.get()
        self.assertFalse(value["ownedRelinquished"])
        self.assertTrue(value["manualRecoveryRequired"])
        self.assertFalse(value["writeAvailable"])
        count=len(self.calls)
        self.assertEqual(self.native.apply(dict(token=TOKEN,watts=21,confirmed=True))["code"],"manual_recovery_required")
        self.assertEqual(len(self.calls),count)

    def test_oem_running_readback_is_available_but_apply_does_not_invoke_runner(self):
        self.response=report(oemControllerActive=True,writeAvailable=False)
        value=self.native.get()
        self.assertTrue(value["ok"]);self.assertTrue(value["available"])
        self.assertEqual(value["limitsWatts"],BASE)
        self.assertFalse(value["writeAvailable"])
        self.assertEqual(value["writeBlockedReason"],"oem_controller_active")
        count=len(self.calls)
        self.assertEqual(self.native.apply(dict(token=TOKEN,watts=20,confirmed=True))["code"],"oem_controller_active")
        self.assertEqual(len(self.calls),count)
    def test_oem_veto_does_not_discard_owned_original_baseline(self):
        self.native.get();self.response=report(limitsWatts=dict.fromkeys(m.KEYS,20),changed=True)
        self.assertTrue(self.native.apply(dict(token=TOKEN,watts=20,confirmed=True))["ok"])
        self.response=report(limitsWatts=dict.fromkeys(m.KEYS,20),oemControllerActive=True,writeAvailable=False)
        self.assertTrue(self.native.get()["restoreAvailable"])
        count=len(self.calls)
        self.assertEqual(self.native.restore(dict(token=TOKEN,confirmed=True))["code"],"oem_controller_active")
        self.assertEqual(len(self.calls),count)
        self.response=report(limitsWatts=dict.fromkeys(m.KEYS,20));self.native.get()
        self.response=report(changed=True)
        self.assertTrue(self.native.restore(dict(token=TOKEN,confirmed=True))["ok"])
        self.assertEqual(self.calls[-1]["baseline"],BASE)
    def test_unknown_operation_keeps_ui_write_disabled_even_after_a_good_read(self):
        self.native.get();self.response=report(limitsWatts=dict.fromkeys(m.KEYS,18),changed=True)
        self.assertFalse(self.native.apply(dict(token=TOKEN,watts=17,confirmed=True))["ok"])
        self.response=report();value=self.native.get()
        self.assertTrue(value["available"])
        self.assertFalse(value["writeAvailable"])
        self.assertEqual(value["writeBlockedReason"],"manual_recovery_required")

    def test_success_without_exact_readback_never_becomes_owned(self):
        self.native.get();self.response=report(limitsWatts=dict.fromkeys(m.KEYS,18),changed=True)
        self.assertEqual(self.native.apply(dict(token=TOKEN,watts=17,confirmed=True))["code"],"confirmation_mismatch")
        self.assertEqual(self.native.apply(dict(token=TOKEN,watts=17,confirmed=True))["code"],"manual_recovery_required")
    def test_native_timeout_stays_unknown_and_blocks_later_writes(self):
        self.native.get()
        self.native._run_cmd=lambda *a,**k: (_ for _ in ()).throw(subprocess.TimeoutExpired("fake",24))
        value=self.native.apply(dict(token=TOKEN,watts=17,confirmed=True))
        self.assertEqual(value["code"],"operation_unknown")
        self.assertTrue(value["recoveryRequired"])
        self.assertEqual(self.native.apply(dict(token=TOKEN,watts=17,confirmed=True))["code"],"manual_recovery_required")
    def test_invalid_report_and_late_identity_are_rejected(self):
        for overrides in [dict(hardwareValidated=True),dict(maximumWatts=True),dict(limitsWatts=dict(spl=35,sppt=17,fppt=25)),
                          dict(limitsWatts=dict(spl=True,sppt=17,fppt=25)),dict(token="bad"),dict(readbackKind="saved-setting"),dict(changed="true"),dict(available=False),dict(oemControllerActive=True),dict(writeAvailable="true")]:
            self.response=report(**overrides);self.assertFalse(self.native.get()["ok"])
        self.response=report()
        self.native._run_cmd=lambda *a,**k:(setattr(self,"identity",{}),json.dumps(self.response),"")
        self.assertEqual(self.native.get()["code"],"device_changed")
    def test_failed_get_clears_previous_grant(self):
        self.native.get();self.response=dict(ok=False,available=False,code="unavailable",recoveryRequired=False,changed=False)
        self.native.get()
        self.assertEqual(self.native.apply(dict(token=TOKEN,watts=17,confirmed=True))["code"],"fresh_read_required")

FAKE=r'''
$script:token='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$script:limits=@{spl=25;sppt=30;fppt=35};$script:writes=[Collections.Generic.List[string]]::new()
$script:writeCount=0;$script:gateCount=0;$script:failureSpent=$false
function OpenProvider {$script:identity='retained-exact-device';$script:oemActive=$false}
function Gate {
    $script:gateCount++
    if ($scenario -eq 'device-drift' -and $script:gateCount -ge 2) {throw 'device_changed'}
    if ($scenario -eq 'oem-active' -and $script:gateCount -ge 2) {throw 'oem_controller_active'}
    if ($scenario -eq 'power-drift' -and $script:gateCount -ge 2) {return $false}
    return $true
}
function Invoke-CimMethod {
    param($InputObject,[string]$MethodName,$Arguments,$OperationTimeoutSec)
    $key=$null
    foreach($k in @('spl','sppt','fppt')) {if ($script:registers[$k] -eq $Arguments.Device_ID) {$key=$k}}
    if (!$key -or $OperationTimeoutSec -ne 1) {throw 'unexpected_mock_operation'}
    if ($MethodName -eq 'DSTS') {
        if ($scenario -eq 'bad-readback') {return @{device_status=[uint32]0x00090011}}
        return @{device_status=[uint32](0x10000 -bor $script:limits[$key])}
    }
    if ($MethodName -ne 'DEVS') {throw 'unexpected_mock_operation'}
    $script:writeCount++;$watts=[int]$Arguments.Control_status
    $script:writes.Add("$key`:$watts")
    $fail=$script:writeCount -eq 2 -and !$script:failureSpent
    if ($fail -and @('throw-before','throw-after','ignore','reject','external') -contains $scenario) {
        $script:failureSpent=$true
        if ($scenario -eq 'throw-before') {throw 'native_failure'}
        if ($scenario -eq 'ignore') {return @{result=1}}
        if ($scenario -eq 'reject') {return @{result=0}}
        $script:limits[$key]=$watts
        if ($scenario -eq 'external') {$script:limits[$key]=19;throw 'native_failure'}
        throw 'native_failure'
    }
    $script:limits[$key]=$watts
    return @{result=1}
}
$r=Operate
@{result=$r;writes=@($script:writes);limits=$script:limits}|ConvertTo-Json -Compress -Depth 8
'''

class ActualPowerShellTransaction(unittest.TestCase):
    def run_case(self,scenario="ok",initial=None,**extra):
        request={"action":"apply","confirmed":True,"token":TOKEN,"expected":dict(BASE),"externalPower":True,"watts":17,**extra}
        data=base64.b64encode(json.dumps(request).encode()).decode()
        fake=FAKE
        if initial is not None:
            fake=fake.replace("$script:limits=@{spl=25;sppt=30;fppt=35}","$script:limits=@{spl=%d;sppt=%d;fppt=%d}"%(initial["spl"],initial["sppt"],initial["fppt"]))
        source=m._SCRIPT+"\n$scenario='"+scenario+"'\n"+fake
        with tempfile.TemporaryDirectory(prefix="Playhub-Tdp-Inert-") as folder:
            f=Path(folder)/"fake.ps1";f.write_text(source,encoding="utf-8-sig")
            r=subprocess.run([r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",str(f),"-RequestBase64",data],capture_output=True,text=True,timeout=10,creationflags=0x08000000)
        self.assertEqual(r.returncode,0,r.stderr)
        return json.loads(r.stdout)
    def test_get_never_writes(self):
        v=self.run_case(action="get");self.assertTrue(v["result"]["ok"]);self.assertEqual(v["writes"],[])
    def test_three_actual_writes_and_readbacks(self):
        v=self.run_case();self.assertTrue(v["result"]["ok"]);self.assertEqual(v["writes"],["spl:17","sppt:17","fppt:17"])
        self.assertEqual(v["result"]["limitsWatts"],dict.fromkeys(m.KEYS,17))
    def test_stale_token_expected_and_out_of_range_never_write(self):
        for overrides in [dict(token="b"*64),dict(expected=dict.fromkeys(m.KEYS,17)),dict(watts=True),dict(watts=6),dict(watts=36),dict(confirmed="true")]:
            v=self.run_case(**overrides);self.assertFalse(v["result"]["ok"]);self.assertEqual(v["writes"],[])
    def test_reserved_bits_never_become_a_value_or_write(self):
        v=self.run_case("bad-readback");self.assertFalse(v["result"]["available"]);self.assertEqual(v["writes"],[])
    def test_prewrite_identity_oem_or_power_drift_never_writes(self):
        for scenario in ["device-drift","oem-active","power-drift"]:
            v=self.run_case(scenario);self.assertFalse(v["result"]["ok"]);self.assertEqual(v["writes"],[])
    def test_throw_before_after_reject_and_ignored_write_restore_actual_values(self):
        for scenario in ["throw-before","throw-after","reject","ignore"]:
            v=self.run_case(scenario);self.assertFalse(v["result"]["ok"])
            self.assertTrue(v["result"]["rollbackConfirmed"],v)
            self.assertEqual(v["limits"],BASE,v)
            self.assertFalse(v["result"]["recoveryRequired"],v)
    def test_foreign_written_value_is_preserved_and_recovery_stays_unknown(self):
        v=self.run_case("external");self.assertFalse(v["result"]["ok"])
        self.assertTrue(v["result"]["recoveryRequired"])
        self.assertFalse(v["result"]["available"])
        self.assertEqual(v["limits"]["sppt"],19)
        self.assertNotIn("sppt:30",v["writes"])
    def test_mixed_restore_decreases_low_limits_before_increasing_boost(self):
        initial=dict.fromkeys(m.KEYS,17);baseline=dict(spl=7,sppt=10,fppt=54)
        v=self.run_case(initial=initial,action="restore",expected=initial,baseline=baseline)
        self.assertTrue(v["result"]["ok"],v)
        self.assertEqual(v["writes"],["spl:7","sppt:10","fppt:54"])
        self.assertEqual(v["limits"],baseline)
    def test_mixed_restore_decreases_boost_before_increasing_low_limits(self):
        initial=dict(spl=7,sppt=10,fppt=54);baseline=dict.fromkeys(m.KEYS,17)
        v=self.run_case(initial=initial,action="restore",expected=initial,baseline=baseline)
        self.assertTrue(v["result"]["ok"],v)
        self.assertEqual(v["writes"],["fppt:17","sppt:17","spl:17"])
        self.assertEqual(v["limits"],baseline)
    def test_mixed_restore_throw_rolls_back_in_reverse_actual_write_order(self):
        initial=dict.fromkeys(m.KEYS,17);baseline=dict(spl=7,sppt=10,fppt=54)
        v=self.run_case("throw-after",initial=initial,action="restore",expected=initial,baseline=baseline)
        self.assertFalse(v["result"]["ok"])
        self.assertTrue(v["result"]["rollbackConfirmed"],v)
        self.assertEqual(v["writes"],["spl:7","sppt:10","sppt:17","spl:17"])
        self.assertEqual(v["limits"],initial)
    def test_get_rechecks_device_after_the_three_slow_reads(self):
        v=self.run_case("device-drift",action="get")
        self.assertFalse(v["result"]["ok"])
        self.assertFalse(v["result"]["available"])
        self.assertEqual(v["writes"],[])

    def test_increase_orders_boost_before_sustained_limit(self):
        v=self.run_case(watts=35);self.assertTrue(v["result"]["ok"])
        self.assertEqual(v["writes"],["sppt:35","spl:35"])

# Exercise the REAL production Gate too; only its external APIs are faked.
OEM_FAKE = r'''
Add-Type -TypeDefinition 'public static class PlayhubHandheldPowerState {public struct S {public byte ACLineStatus;} public static bool GetSystemPowerStatus(out S s){s=new S();s.ACLineStatus=1;return true;}}'
$script:oemQueries=0;$script:controllerQueries=0
function DeviceIdentity {return 'retained-exact-device'}
function Get-CimInstance {
    param($ClassName,$Namespace,$Filter,$OperationTimeoutSec)
    if ($OperationTimeoutSec -ne 1) {throw 'unexpected_query'}
    if ($ClassName -eq 'Win32_Process') {
        if ($Filter -notlike '*GHelper.exe*' -or $Filter -notlike '*HandheldCompanion.exe*' -or
            $Filter -notlike '*HandheldControlPanel.exe*' -or $Filter -notlike '*Handheld Control Panel.exe*') {throw 'unexpected_query'}
        $script:controllerQueries++
        if ($scenario -eq 'controller-query-fails') {throw 'query_failed'}
        $name=$null
        if ($scenario.StartsWith('controller-')) {$name=$scenario.Substring(11)}
        if ($scenario -eq 'controller-starts-late') {$name=if($script:controllerQueries -ge 2){'GHelper.exe'}else{$null}}
        if ($name) {return [pscustomobject]@{Name=$name;ProcessId=712}}
        return
    }
    if ($ClassName -ne 'Win32_Service' -or $Filter -notlike '*ArmouryCrateSEService*') {throw 'unexpected_query'}
    $script:oemQueries++
    if ($scenario -eq 'oem-query-fails') {throw 'query_failed'}
    $running=$scenario -eq 'oem-running' -or ($scenario -eq 'oem-starts-late' -and $script:oemQueries -ge 2)
    $state=if($running){'Running'}else{'Stopped'}
    if ($scenario.StartsWith('state-')) {$state=$scenario.Substring(6)}
    if ($scenario -eq 'state-null') {$state=$null}
    if ($scenario -eq 'late-unknown' -and $script:oemQueries -ge 2) {$state='Unknown'}
    return [pscustomobject]@{Name='AsusAppService';State=$state}
}
'''

class ActualOemGate(unittest.TestCase):
    def run_case(self,action,scenario="oem-running"):
        request={"action":action,"confirmed":True,"token":TOKEN,"expected":dict(BASE),
                 "externalPower":True,"watts":17,"baseline":dict(BASE)}
        payload=base64.b64encode(json.dumps(request).encode()).decode()
        # Preserve the actual Operate/Gate/ReadLimits/WriteRegister implementations.
        fake=FAKE[:FAKE.index("function Gate {")]+FAKE[FAKE.index("function Invoke-CimMethod {"):]
        fake=fake.replace(";$script:oemActive=$false", "")
        source=m._SCRIPT+"\n$scenario='"+scenario+"'\n"+OEM_FAKE+fake
        with tempfile.TemporaryDirectory(prefix="Playhub-Tdp-Oem-Inert-") as directory:
            file=Path(directory)/"fake.ps1";file.write_text(source,encoding="utf-8-sig")
            result=subprocess.run([r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",str(file),"-RequestBase64",payload],capture_output=True,text=True,timeout=10,creationflags=0x08000000)
        self.assertEqual(result.returncode,0,result.stderr)
        return json.loads(result.stdout)
    def test_real_gate_allows_dsts_get_with_oem_active(self):
        value=self.run_case("get")
        self.assertEqual(value["writes"],[])
        self.assertTrue(value["result"]["available"])
        self.assertTrue(value["result"]["ok"])
        self.assertEqual(value["result"]["limitsWatts"],BASE)
        self.assertTrue(value["result"]["oemControllerActive"])
        self.assertFalse(value["result"]["writeAvailable"])
    def test_each_competing_controller_keeps_actual_readback_but_vetoes_devs(self):
        for name in ('GHelper.exe','HandheldCompanion.exe','HandheldControlPanel.exe','Handheld Control Panel.exe'):
            with self.subTest(controller=name):
                value=self.run_case('get','controller-'+name)
                self.assertTrue(value['result']['ok']);self.assertTrue(value['result']['available'])
                self.assertEqual(value['result']['limitsWatts'],BASE);self.assertEqual(value['writes'],[])
                self.assertTrue(value['result']['oemControllerActive']);self.assertFalse(value['result']['writeAvailable'])
                for action in ('apply','restore'):
                    result=self.run_case(action,'controller-'+name)
                    self.assertEqual(result['result']['code'],'oem_controller_active')
                    self.assertFalse(result['result']['writeAvailable']);self.assertEqual(result['writes'],[])
    def test_late_competing_controller_cannot_reuse_an_earlier_clear_observation(self):
        value=self.run_case('get','controller-starts-late')
        self.assertTrue(value['result']['ok']);self.assertTrue(value['result']['oemControllerActive'])
        self.assertFalse(value['result']['writeAvailable']);self.assertEqual(value['writes'],[])
        value=self.run_case('apply','controller-starts-late')
        self.assertEqual(value['result']['code'],'oem_controller_active');self.assertEqual(value['writes'],[])
    def test_unknown_competing_owner_query_never_grants_write(self):
        for action in ('get','apply','restore'):
            value=self.run_case(action,'controller-query-fails')
            self.assertFalse(value['result']['available']);self.assertFalse(value['result']['writeAvailable'])
            self.assertIsNone(value['result']['oemControllerActive']);self.assertEqual(value['writes'],[])
    def test_real_gate_vetoes_apply_and_restore_before_devs(self):
        for action in ["apply","restore"]:
            value=self.run_case(action)
            self.assertFalse(value["result"]["ok"])
            self.assertEqual(value["result"]["code"],"oem_controller_active")
            self.assertEqual(value["writes"],[])
            self.assertEqual(value["limits"],BASE)
            self.assertTrue(value["result"]["oemControllerActive"])
            self.assertFalse(value["result"]["writeAvailable"])
    def test_get_observes_a_late_oem_start_as_read_only(self):
        value=self.run_case("get","oem-starts-late")
        self.assertTrue(value["result"]["ok"])
        self.assertTrue(value["result"]["oemControllerActive"])
        self.assertFalse(value["result"]["writeAvailable"])
        self.assertEqual(value["writes"],[])
    def test_late_oem_start_vetoes_first_write(self):
        value=self.run_case("apply","oem-starts-late")
        self.assertFalse(value["result"]["ok"])
        self.assertEqual(value["result"]["code"],"oem_controller_active")
        self.assertEqual(value["writes"],[])
    def test_only_stopped_or_absent_services_allow_writes(self):
        value=self.run_case("apply","state-Stopped")
        self.assertTrue(value["result"]["ok"])
        self.assertEqual(len(value["writes"]),3)
    def test_pending_and_paused_services_are_active_and_never_allow_devs(self):
        for state in ["Start Pending","Stop Pending","Continue Pending","Pause Pending","Paused","StartPending"]:
            value=self.run_case("get","state-"+state)
            self.assertTrue(value["result"]["ok"])
            self.assertTrue(value["result"]["oemControllerActive"])
            self.assertFalse(value["result"]["writeAvailable"])
            for action in ["apply","restore"]:
                value=self.run_case(action,"state-"+state)
                self.assertEqual(value["result"]["code"],"oem_controller_active")
                self.assertEqual(value["writes"],[])
    def test_unknown_or_missing_service_state_stays_unknown_without_a_write(self):
        for state in ["Unknown","null","Other"]:
            for action in ["get","apply","restore"]:
                value=self.run_case(action,"state-"+state)
                self.assertFalse(value["result"]["available"])
                self.assertIsNone(value["result"]["oemControllerActive"])
                self.assertFalse(value["result"]["writeAvailable"])
                self.assertEqual(value["writes"],[])
    def test_second_unknown_query_does_not_reuse_the_first_stopped_observation(self):
        value=self.run_case("get","late-unknown")
        self.assertFalse(value["result"]["available"])
        self.assertIsNone(value["result"]["oemControllerActive"])
        self.assertFalse(value["result"]["writeAvailable"])
        self.assertEqual(value["writes"],[])

    def test_failed_oem_query_never_fabricates_inactive_or_write_permission(self):
        value=self.run_case("get","oem-query-fails")
        self.assertFalse(value["result"]["available"])
        self.assertIsNone(value["result"]["oemControllerActive"])
        self.assertFalse(value["result"]["writeAvailable"])
        self.assertEqual(value["writes"],[])

if __name__=="__main__":unittest.main(verbosity=2)

