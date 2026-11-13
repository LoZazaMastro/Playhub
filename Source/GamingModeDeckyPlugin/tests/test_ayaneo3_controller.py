"""Inert policy/protocol tests; native tests are independently supplied by peer."""
import ast
import colorsys
from dataclasses import replace
import importlib.util
from pathlib import Path
import sys
import unittest

root=Path(__file__).resolve().parents[1]
def load(name,file):
    spec=importlib.util.spec_from_file_location(name,root/'quick_settings'/file)
    module=importlib.util.module_from_spec(spec);sys.modules[name]=module;spec.loader.exec_module(module);return module
m=load('aya_policy','ayaneo3_controller.py');n=load('aya_native','ayaneo3_windows_hid.py')


def status(state):
    data=bytearray(64);data[3]=8;data[18]=2;data[21:23]=b'\x33\x22'
    data[7:11]=bytes((state.left_mode,))+state.left_rgb
    data[11:15]=bytes((state.right_mode,))+state.right_rgb
    data[23]=state.vibration<<4;data[32:34]=bytes(state.modules)
    return bytes(data)


BASE=m.State(1,b'\x20\x40\x60',1,b'\x21\x41\x61',3,(2,0x10))


class Timer:
    def __init__(self,*args):self.daemon=False
    def start(self):pass
    def cancel(self):pass


class Session:
    def __init__(self):
        self.state=BASE;self.binding='exact-parent-caps-path';self.alive=True;self.closed=False
        self.writes=[];self.queries=0;self.failure=None;self.clock=None;self.before=None
    def current(self):return self.alive and not self.closed
    def close(self):self.closed=True
    def exchange(self,command,before_write=None):
        if not self.current():raise RuntimeError('not_current')
        if command==m.CHECK:
            self.queries+=1
            if self.failure=='read-failure' and self.writes:raise RuntimeError('read failed')
            return status(self.state)
        if self.before:self.before()
        if self.failure=='pre-dispatch':raise RuntimeError('lookup failed')
        if before_write:before_write()
        self.writes.append(command)
        target=m.State(command[8],command[9:12],command[12],command[13:16],command[24]>>4,self.state.modules)
        if self.failure=='ignored':return bytes(64)
        if self.failure=='foreign':self.state=replace(self.state,vibration=2);return bytes(64)
        self.state=target
        if self.failure=='after-applied':raise RuntimeError('late failure')
        return bytes(64)


class Transport:
    def __init__(self):self.sessions=[]
    def open(self):
        s=Session();self.sessions.append(s);return s


class Tests(unittest.TestCase):
    def setUp(self):
        self.time=1;self.transport=Transport()
        self.p=m.Ayaneo3Controller(transport_factory=lambda:self.transport,clock=lambda:self.time,timer_factory=Timer)
    def get(self):
        r=self.p.get();self.assertTrue(r['available'],r);return r
    def request(self,**fields):return dict(token=self.get()['token'],**fields)
    def test_decode_strict_protocol(self):
        self.assertEqual(BASE,m.decode(status(BASE)))
        for index,value in ((3,9),(18,1),(21,0),(22,0),(23,0x31),(7,8),(11,8),(32,0),(33,0)):
            data=bytearray(status(BASE));data[index]=value
            with self.subTest(index=index),self.assertRaises(ValueError):m.decode(bytes(data))
        for data in (bytes(63),bytes(65),bytearray(status(BASE))):
            with self.assertRaises(ValueError):m.decode(data)
    def test_native_command_allowlist(self):
        self.assertTrue(n.safe_command(m.CHECK));self.assertTrue(n.safe_command(m.packet(BASE)))
        for cmd in (bytes([0,0,0,0,5])+bytes(60),bytes([0,0,0,0,10,2])+bytes(59),bytes(64)):
            self.assertFalse(n.safe_command(cmd))
        for offset in (0,3,4,20,32,37,38,40):
            data=bytearray(m.packet(BASE));data[offset]^=1
            data[1:3]=sum(data[7:]).to_bytes(2,'little')
            self.assertFalse(n.safe_command(bytes(data)),offset)
    def test_public_rgb_request_matches_pinned_protocol_vectors(self):
        # Outputs independently captured from Ayaneo3Companion bed273c04ccf1c9bfd31caf1ecc4c4c6b20e23c7.
        vectors={
            '123456':'0072022109000000011229560112295600000000000033223000000000000000010000000064640000000000000000000000000000000000000000000000000000',
            'ffffff':'004a07210900000001ffffff01ffffff00000000000033223000000000000000010000000064640000000000000000000000000000000000000000000000000000',
            '000000':'0050012109000000010000000100000000000000000033223000000000000000010000000064640000000000000000000000000000000000000000000000000000',
            'aa0099':'009e03210900000001aa007d01aa007d00000000000033223000000000000000010000000064640000000000000000000000000000000000000000000000000000',
        }
        current=BASE
        for color,expected in vectors.items():
            target=self.p._requested(current,{'rgbMode':'solid','rgbHex':color,'vibration':'high'})
            self.assertEqual(bytes.fromhex(expected),m.packet(target))
    def test_initial_get_has_no_configuration_write(self):
        report=self.get();session=self.transport.sessions[-1]
        self.assertEqual([],session.writes);self.assertEqual(1,session.queries)
        self.assertFalse(report['brightnessReadbackAvailable']);self.assertFalse(report['hardwareValidated'])
    def test_apply_twice_restore_original_server_baseline(self):
        self.assertTrue(self.p.apply(self.request(rgbMode='solid',rgbHex='ff0000'))['ok'])
        session=self.transport.sessions[-1]
        self.assertTrue(self.p.apply(self.request(vibration='low'))['ok'])
        self.assertEqual(BASE,self.p._baseline)
        self.assertTrue(self.p.restore({'token':self.get()['token']})['ok'])
        self.assertEqual(BASE,session.state);self.assertIsNone(self.p._baseline)
        self.assertEqual(3,len(session.writes))
    def test_expired_token_does_not_write(self):
        request=self.request(vibration='low');session=self.transport.sessions[-1]
        self.time=4
        self.assertFalse(self.p.apply(request)['ok']);self.assertEqual([],session.writes)
        self.assertFalse(self.p._uncertain)
    def test_expiry_after_last_native_getter_no_write(self):
        request=self.request(vibration='low');session=self.transport.sessions[-1]
        session.before=lambda:setattr(self,'time',4)
        self.assertEqual('not_dispatched',self.p.apply(request)['reason'])
        self.assertEqual([],session.writes);self.assertFalse(self.p._uncertain);self.assertIsNone(self.p._baseline)
    def test_pre_dispatch_failure_does_not_latch_uncertainty(self):
        request=self.request(vibration='low');session=self.transport.sessions[-1];session.failure='pre-dispatch'
        self.assertEqual('not_dispatched',self.p.apply(request)['reason'])
        self.assertIsNone(self.p._baseline);self.assertFalse(self.p._uncertain)
    def test_invalid_request_does_not_write(self):
        for fields in ({'vibration':'x'},{'rgbMode':'solid','rgbHex':'#00ff00'},{'rgbMode':4,'rgbHex':'ffffff'},
                       {'token':'foreign','vibration':'low'},{'reset':True},{'save':True},{'eject':'left'}):
            request=self.request();request.update(fields)
            self.assertFalse(self.p.apply(request)['ok'])
            self.assertEqual([],self.transport.sessions[-1].writes)
    def test_known_foreign_state_no_stale_restore(self):
        self.assertTrue(self.p.apply(self.request(vibration='low'))['ok'])
        session=self.transport.sessions[-1];session.state=replace(session.state,vibration=2)
        report=self.get();self.assertFalse(report['restoreAvailable'])
        self.assertFalse(self.p.restore({'token':report['token']})['ok'])
        self.assertEqual(1,len(session.writes));self.assertEqual(2,session.state.vibration)
    def test_module_replacement_rejects_stale_apply(self):
        request=self.request(vibration='low');session=self.transport.sessions[-1]
        session.state=replace(session.state,modules=(4,0x12))
        self.assertFalse(self.p.apply(request)['ok']);self.assertEqual([],session.writes)
    def test_ignored_second_write_preserves_original_baseline_and_target(self):
        self.assertTrue(self.p.apply(self.request(vibration='low'))['ok'])
        session=self.transport.sessions[-1];first=session.state
        request=self.request(vibration='medium');session.failure='ignored'
        result=self.p.apply(request)
        self.assertEqual('write_not_applied',result['reason'])
        self.assertEqual(BASE,self.p._baseline);self.assertEqual(first,self.p._target)
        session.failure=None
        self.assertTrue(self.p.restore({'token':self.get()['token']})['ok'])
        self.assertEqual(BASE,session.state)
    def test_applied_but_ack_missing_only_readback_confirms(self):
        request=self.request(vibration='low');session=self.transport.sessions[-1];session.failure='after-applied'
        result=self.p.apply(request)
        self.assertTrue(result['ok']);self.assertFalse(result['acknowledged']);self.assertTrue(result['verified'])
        self.assertEqual(1,len(session.writes))
    def test_unknown_write_failure_blocks_new_apply(self):
        request=self.request(vibration='low');session=self.transport.sessions[-1];session.failure='read-failure'
        self.assertTrue(self.p.apply(request)['manualRecoveryRequired'])
        count=len(session.writes)
        self.assertFalse(self.p.apply(request)['ok']);self.assertEqual(count,len(session.writes))
        session.failure=None
        report=self.get();self.assertFalse(report['writeAvailable'])
        self.assertTrue(self.p.restore({'token':report['token']})['ok']);self.assertEqual(BASE,session.state)
    def test_reconnect_cannot_restore_prior_connection_baseline(self):
        self.assertTrue(self.p.apply(self.request(vibration='low'))['ok'])
        old=self.transport.sessions[-1];old.alive=False
        self.assertFalse(self.p.get()['available'])
        new_report=self.p.get();new=self.transport.sessions[-1]
        self.assertFalse(new_report['available']);self.assertFalse(new_report['writeAvailable'])
        self.assertFalse(self.p.restore({'token':'no-old-generation-authority'})['ok'])
        self.assertEqual([],new.writes)
    def test_readonly_idle_releases_exclusive_collection(self):
        self.get();session=self.transport.sessions[-1];nonce=self.p._nonce;self.time=4
        self.p._idle(nonce);self.assertTrue(session.closed)
        self.get();new=self.transport.sessions[-1]
        self.p._idle(nonce);self.assertFalse(new.closed)
    def test_owned_idle_retains_only_until_explicit_restore_or_dispose(self):
        self.assertTrue(self.p.apply(self.request(vibration='low'))['ok'])
        session=self.transport.sessions[-1];self.time=4
        self.p._idle(self.p._nonce);self.assertFalse(session.closed)
        self.assertTrue(self.p.dispose()['ok']);self.assertTrue(session.closed);self.assertEqual(BASE,session.state)
    def test_dispose_preserves_foreign_configuration(self):
        self.assertTrue(self.p.apply(self.request(vibration='low'))['ok'])
        session=self.transport.sessions[-1];session.state=replace(session.state,vibration=2)
        self.assertTrue(self.p.dispose()['ok']);self.assertEqual(1,len(session.writes));self.assertEqual(2,session.state.vibration)
    def test_post_dispose_no_reopen(self):
        self.p.dispose();self.assertFalse(self.p.get()['available']);self.assertEqual([],self.transport.sessions)


if __name__=='__main__':unittest.main()
