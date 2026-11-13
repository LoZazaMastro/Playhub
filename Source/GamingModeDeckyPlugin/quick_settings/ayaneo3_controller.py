"""AYANEO 3 RGB/vibration configuration, explicit operations with readback.

Wire facts: Rayekkk/Ayaneo3Companion@bed273c (BSD-3-Clause, notices supplied).
No gamepad reports, rumble test, button map, mode switch, reset, eject or save.
"""
from dataclasses import dataclass, replace
import colorsys
import hashlib
import importlib.util
import json
from pathlib import Path
import threading
import time
import uuid

CHECK=bytes((0,0,0,0,8))+bytes(60)
MODES={'solid':1,'pulse':2,'rainbow':3,'off':255}
VIBRATIONS={'low':1,'medium':2,'high':3,'off':4}
LEFT=frozenset((2,4,6,8,0x42,0x44,0x46,0x48))
RIGHT=frozenset((0x10,0x12,0x14,0x16,0x50,0x52,0x54,0x56))


@dataclass(frozen=True)
class State:
    left_mode:int
    left_rgb:bytes
    right_mode:int
    right_rgb:bytes
    vibration:int
    modules:tuple


def decode(report):
    if (type(report) is not bytes or len(report)!=64 or report[3]!=8 or report[18]!=2
            or report[21:23]!=b'\x33\x22' or report[23]&15
            or report[7] not in MODES.values() or report[11] not in MODES.values()
            or report[23]>>4 not in VIBRATIONS.values()
            or report[32] not in LEFT or report[33] not in RIGHT):
        raise ValueError('unknown_controller_state')
    return State(report[7],report[8:11],report[11],report[12:15],report[23]>>4,(report[32],report[33]))


def packet(state):
    data=bytearray(65);data[3:5]=b'\x21\x09'
    data[8:12]=bytes((state.left_mode,))+state.left_rgb
    data[12:16]=bytes((state.right_mode,))+state.right_rgb
    data[22:25]=bytes((0x33,0x22,state.vibration<<4))
    data[32]=1;data[37:39]=b'\x64\x64'
    data[1:3]=sum(data[7:]).to_bytes(2,'little')
    return bytes(data)


def matches(current,expected):
    if (current.modules!=expected.modules or current.vibration!=expected.vibration
            or current.left_mode!=expected.left_mode or current.right_mode!=expected.right_mode):return False
    for mode,a,b in ((expected.left_mode,current.left_rgb,expected.left_rgb),
                     (expected.right_mode,current.right_rgb,expected.right_rgb)):
        if mode in (1,2) and any(abs(x-y)>1 for x,y in zip(a,b)):return False
    return True


def _hue(data,degrees):
    h,s,v=colorsys.rgb_to_hsv(*(x/255 for x in data))
    return bytes(round(x*255) for x in colorsys.hsv_to_rgb((h+degrees/360)%1,s,v))


def _native():
    path=Path(__file__).with_name('ayaneo3_windows_hid.py')
    spec=importlib.util.spec_from_file_location('_playhub_ayaneo3_windows_hid',path)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module.WindowsHid()


class Ayaneo3Controller:
    def __init__(self, *, transport_factory=None, clock=None, timer_factory=None):
        self._factory=transport_factory or _native
        self._clock=clock or time.monotonic
        self._timer_factory=timer_factory or threading.Timer
        self._lock=threading.RLock();self._transport=None;self._session=None
        self._nonce=None;self._token=None;self._until=0;self._state=None;self._timer=None
        self._baseline=None;self._target=None;self._uncertain=False;self._disposed=False
        self._owned_nonce=None

    def _drop(self):
        if self._timer is not None: self._timer.cancel();self._timer=None
        if self._session is not None: self._session.close()
        self._session=None;self._nonce=None;self._token=None;self._state=None

    def _idle(self, nonce):
        with self._lock:
            if nonce==self._nonce and self._baseline is None and self._clock()>=self._until:
                self._drop()

    def _arm_idle(self):
        if self._timer is not None:self._timer.cancel();self._timer=None
        if self._baseline is None:
            self._timer=self._timer_factory(2.1,lambda nonce=self._nonce:self._idle(nonce))
            self._timer.daemon=True;self._timer.start()

    def _read(self):
        if self._disposed: raise ValueError('disposed')
        if self._session is None:
            self._transport=self._transport or self._factory()
            self._session=self._transport.open();self._nonce=uuid.uuid4().hex
        if not self._session.current():raise ValueError('device_not_current')
        state=decode(self._session.exchange(CHECK))
        if not self._session.current():raise ValueError('device_changed')
        if self._target is not None and not matches(state,self._target):
            if self._uncertain: raise ValueError('manual_recovery_required')
            # Another confirmed configuration is not ours to restore.
            self._baseline=None;self._target=None
            self._owned_nonce=None
        return state

    def _report(self,state):
        payload=(self._nonce,self._session.binding,state.left_mode,state.left_rgb.hex(),
                 state.right_mode,state.right_rgb.hex(),state.vibration,state.modules)
        self._token=hashlib.sha256(json.dumps(payload,separators=(',',':')).encode()).hexdigest()
        self._until=self._clock()+2.0;self._state=state;self._arm_idle()
        reverse_modes={v:k for k,v in MODES.items()};reverse_vibration={v:k for k,v in VIBRATIONS.items()}
        return {'ok':True,'available':True,'provider':'ayaneo3-hid','token':self._token,
                'writeAvailable':not self._uncertain,'manualRecoveryRequired':self._uncertain,
                'restoreAvailable':self._baseline is not None and self._owned_nonce==self._nonce,
                'readbackKind':'vendor-hid-status',
                'state':{'leftMode':reverse_modes[state.left_mode],'leftRgb':_hue(state.left_rgb,-10).hex(),
                         'rightMode':reverse_modes[state.right_mode],'rightRgb':_hue(state.right_rgb,-10).hex(),
                         'vibration':reverse_vibration[state.vibration]},
                'modules':{'left':state.modules[0],'right':state.modules[1]},
                'brightnessReadbackAvailable':False,'verificationRgbTolerance':1,
                'experimental':True,'hardwareValidated':False}

    def get(self):
        with self._lock:
            try:return self._report(self._read())
            except Exception as error:
                if self._baseline is not None:self._uncertain=True
                self._drop()
                return {'ok':False,'available':False,'provider':'ayaneo3-hid',
                        'reason':str(error) if isinstance(error,ValueError) else 'transport_unavailable',
                        'manualRecoveryRequired':self._uncertain,'writeAvailable':False}

    def _guard(self,request):
        if (self._disposed or type(request) is not dict or type(request.get('token')) is not str
                or request['token']!=self._token or self._session is None
                or self._clock()>=self._until):raise ValueError('fresh_read_required')
        before=self._state
        current=self._read()
        if not matches(current,before) or not self._session.current() or self._clock()>=self._until:
            self._token=None;raise ValueError('state_changed')
        return current

    @staticmethod
    def _requested(current,request):
        if set(request)-{'token','rgbMode','rgbHex','vibration'}:raise ValueError('invalid_request')
        result=current
        if 'rgbMode' in request or 'rgbHex' in request:
            if request.get('rgbMode') not in MODES or type(request.get('rgbHex')) is not str:
                raise ValueError('invalid_rgb')
            text=request['rgbHex']
            if len(text)!=6 or any(c not in '0123456789abcdefABCDEF' for c in text):raise ValueError('invalid_rgb')
            color=_hue(bytes.fromhex(text),10);mode=MODES[request['rgbMode']]
            if mode==255:color=bytes(3)
            result=replace(result,left_mode=mode,left_rgb=color,right_mode=mode,right_rgb=color)
        if 'vibration' in request:
            if request['vibration'] not in VIBRATIONS:raise ValueError('invalid_vibration')
            result=replace(result,vibration=VIBRATIONS[request['vibration']])
        if result==current:raise ValueError('no_change')
        return result

    def _write(self,current,target):
        old_baseline,old_target,old_nonce=self._baseline,self._target,self._owned_nonce
        session,nonce=self._session,self._nonce
        dispatched=False
        acknowledged=False
        try:
            def before_write():
                nonlocal dispatched
                # Last copy-only fence after native current/caps/queue getters.
                if (self._disposed or self._session is not session or self._nonce!=nonce
                        or self._clock()>=self._until):raise ValueError('prewrite_changed')
                if self._baseline is None:
                    self._baseline=current;self._owned_nonce=nonce
                self._target=target;dispatched=True
            try:
                session.exchange(packet(target),before_write=before_write);acknowledged=True
            except Exception:
                if not dispatched:raise ValueError('not_dispatched')
            observed=decode(self._session.exchange(CHECK))
            if not self._session.current():raise ValueError('device_changed')
            if matches(observed,target):
                self._uncertain=False
                return dict(self._report(observed),acknowledged=acknowledged,verified=True)
            if matches(observed,current):
                self._baseline,self._target,self._owned_nonce=old_baseline,old_target,old_nonce
                return dict(self._report(current),ok=False,reason='write_not_applied',verified=True)
            self._uncertain=True;raise ValueError('foreign_or_partial_readback')
        except Exception as error:
            if not dispatched:
                return {'ok':False,'reason':'not_dispatched','writeAvailable':not self._uncertain}
            self._uncertain=True;self._token=None
            return {'ok':False,'reason':str(error) if isinstance(error,ValueError) else 'write_uncertain',
                    'manualRecoveryRequired':True,'writeAvailable':False}

    def apply(self,request):
        with self._lock:
            try:
                if self._uncertain:raise ValueError('manual_recovery_required')
                current=self._guard(request);target=self._requested(current,request)
                return self._write(current,target)
            except Exception as error:
                return {'ok':False,'reason':str(error) if isinstance(error,ValueError) else 'request_unavailable'}

    def restore(self,request):
        with self._lock:
            try:
                current=self._guard(request)
                if (self._baseline is None or self._target is None or not matches(current,self._target)
                        or self._owned_nonce!=self._nonce):
                    raise ValueError('no_owned_baseline')
                baseline=self._baseline
                if current==baseline:
                    self._baseline=None;self._target=None;self._uncertain=False
                    self._owned_nonce=None
                    return self._report(current)
                result=self._write(current,baseline)
                if result.get('ok') is True:
                    self._baseline=None;self._target=None;self._uncertain=False
                    self._owned_nonce=None
                    result=self._report(baseline)
                return result
            except Exception as error:
                return {'ok':False,'reason':str(error) if isinstance(error,ValueError) else 'restore_unavailable'}

    def dispose(self):
        with self._lock:
            result={'ok':True,'restored':False}
            try:
                if self._baseline is not None:
                    read=self.get()
                    if read.get('available') is True and self._target is not None:
                        result=self.restore({'token':read['token']})
                    elif self._uncertain:result={'ok':False,'manualRecoveryRequired':True}
            finally:
                self._disposed=True;self._drop()
            return result
