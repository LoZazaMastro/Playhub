"""GPD Win4 / Mini 2024 V1 rumble settings, with independent config readback.

SPDX-License-Identifier: GPL-3.0-or-later
Protocol: OpenWinControls/libOpenWinControls@63fd9062 (see NOTICE).
No remap, reset, RGB, power, firmware update or arbitrary command RPC.
"""
import hashlib
import importlib.util
from pathlib import Path
import threading
import time
import uuid

PROFILES={('GPD','G1618-04'):('gpd-win-4',(4,7)),
          ('GPD','G1617-01'):('gpd-win-mini',(5,4))}
RUMBLE={'off':0,'low':1,'high':2}


def command(code,page=0,data=b''):
    result=bytearray(32);result[:5]=bytes((1,0xa5,code,0x5a,code^0xff));result[6]=page
    result[8:8+len(data)]=data
    return bytes(result)


class Protocol:
    def __init__(self,session,guard):
        self.session,self.guard=session,guard
        if session.identity not in PROFILES:raise ValueError('unsupported_device')
        self.profile,self.firmware=PROFILES[session.identity]

    def exchange(self,code,page=0,data=b'',before_configuration=None):
        self.guard()
        def before_io():
            self.guard()
            if before_configuration is not None:before_configuration()
        return self.session.exchange(command(code,page,data),reply=code not in (0x21,0x23),before_write=before_io)

    def ready(self,mode):
        response=self.exchange(mode)
        if type(response) is not bytes or len(response)!=64 or response[8]!=0xaa:
            raise ValueError('controller_not_ready')
        if tuple(response[11:13])!=self.firmware:raise ValueError('unknown_firmware')
        return tuple(response[9:13])

    def read(self):
        version=self.ready(0x10)
        parts=[self.exchange(0x11,i) for i in range(2)]
        if any(type(x) is not bytes or len(x)!=64 for x in parts):raise ValueError('short_configuration')
        current=b''.join(parts)
        response=self.exchange(0x12)
        if type(response) is not bytes or len(response)!=64 or int.from_bytes(response[24:26],'little')!=sum(current):
            raise ValueError('configuration_checksum_mismatch')
        if current[66] not in RUMBLE.values():raise ValueError('unknown_rumble')
        self.guard()
        return current,version

    def stable(self):
        first=self.read();second=self.read()
        if first!=second:raise ValueError('configuration_not_stable')
        return first

    def write(self,target,before_configuration,version):
        if self.ready(0x20)!=version:raise ValueError('firmware_changed_before_write')
        for i in range(8):
            self.exchange(0x21,i,target[i*16:(i+1)*16],before_configuration)
        response=self.exchange(0x22)
        if type(response) is not bytes or len(response)!=64 or int.from_bytes(response[24:26],'little')!=sum(target):
            raise ValueError('staged_checksum_mismatch')
        self.exchange(0x23,before_configuration=before_configuration)  # one commit, never retried.


def _native():
    path=Path(__file__).with_name('gpd_v1_windows_hid.py')
    spec=importlib.util.spec_from_file_location('_playhub_gpd_v1_hid',path)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module.WindowsHid()


class GpdV1Controller:
    """Long-lived server-owned controller; dispose only releases its handle.

    Every mutation requires the latest short-lived get token; baseline is never
    accepted from the client. An uncertain write cannot be cleared by a get.
    """
    def __init__(self,*,transport_factory=None,clock=None,timer_factory=None):
        self._factory=transport_factory or _native;self._clock=clock or time.monotonic
        self._lock=threading.Lock();self._session=None;self._nonce=None
        self._token=None;self._state=None;self._until=0;self._version=None
        self._baseline=None;self._target=None;self._owned_version=None;self._uncertain=False;self._disposed=False
        self._timer=None;self._timer_factory=timer_factory or threading.Timer

    def _guard(self,session,nonce,deadline):
        # Last copy-only check after native tuple/caps/current getters.
        if (self._disposed or self._session is not session or self._nonce!=nonce
                or session.closed or self._clock()>=deadline):raise ValueError('operation_expired_or_replaced')

    def _protocol(self,deadline):
        if self._disposed:raise ValueError('disposed')
        if self._session is None:
            self._session=self._factory().open();self._nonce=uuid.uuid4().hex
        session,nonce=self._session,self._nonce
        if not session.current():raise ValueError('device_not_current')
        guard=lambda:self._guard(session,nonce,deadline)
        guard()
        return Protocol(session,guard)

    def _idle(self,nonce):
        with self._lock:
            if self._nonce==nonce and self._baseline is None and self._clock()>=self._until:self._drop()

    def _drop(self):
        if self._timer is not None:self._timer.cancel();self._timer=None
        if self._session is not None:self._session.close()
        self._session=None;self._nonce=None;self._token=None;self._state=None
        self._baseline=None;self._target=None;self._owned_version=None  # no old physical/session baseline.

    def _report(self,state,version,**extra):
        self._state=state;self._version=version
        self._token=hashlib.sha256((self._nonce+self._session.binding).encode()+bytes(version)+state).hexdigest()
        self._until=self._clock()+3
        if self._timer is not None:self._timer.cancel();self._timer=None
        if self._baseline is None:
            self._timer=self._timer_factory(3.1,lambda nonce=self._nonce:self._idle(nonce))
            self._timer.daemon=True;self._timer.start()
        return dict(ok=True,available=True,provider='gpd-v1-hid',profile=PROFILES[self._session.identity][0],
            token=self._token,writeAvailable=not self._uncertain,restoreAvailable=self._baseline is not None and not self._uncertain,
            recoveryRequired=self._uncertain,readbackKind='gpd-v1-config128-checksum-double-read',
            state={'rumble':next(k for k,v in RUMBLE.items() if v==state[66])},
            firmware={'x':list(version[:2]),'k':list(version[2:])},
            experimental=True,hardwareValidated=False,**extra)

    def _failure(self,error):
        return dict(ok=False,available=False,provider='gpd-v1-hid',writeAvailable=False,
            restoreAvailable=False,recoveryRequired=self._uncertain,reason=str(error))

    def get(self):
        if not self._lock.acquire(False):return self._failure(ValueError('busy'))
        try:
            p=self._protocol(self._clock()+4);state,version=p.stable()
            if self._target is not None and (state!=self._target or version!=self._owned_version):
                if not self._uncertain:
                    self._baseline=None;self._target=None;self._owned_version=None  # preserve OEM/firmware change.
            return self._report(state,version)
        except Exception as error:
            self._drop();return self._failure(error)
        finally:self._lock.release()

    def _request(self,request,restore):
        if type(request) is not dict or set(request)!=( {'token'} if restore else {'token','rumble'}):raise ValueError('invalid_request')
        if type(request['token']) is not str or request['token']!=self._token or self._clock()>=self._until:
            raise ValueError('fresh_read_required')
        if self._uncertain:raise ValueError('recovery_required')
        if restore and self._baseline is None:raise ValueError('nothing_to_restore')
        if not restore and (type(request['rumble']) is not str or request['rumble'] not in RUMBLE):raise ValueError('invalid_rumble')
        before=self._state;deadline=min(self._until,self._clock()+4)
        self._token=None
        p=self._protocol(deadline);observed,version=p.stable()
        if observed!=before or version!=self._version:
            if self._target is not None and (observed!=self._target or version!=self._owned_version):
                self._baseline=None;self._target=None;self._owned_version=None
            raise ValueError('configuration_changed')
        if self._target is not None and observed!=self._target:raise ValueError('ownership_changed')
        target=self._baseline if restore else before[:66]+bytes((RUMBLE[request['rumble']],))+before[67:]
        if target==before:
            if restore:self._baseline=None;self._target=None;self._owned_version=None
            return self._report(before,version,verified=True,changed=False)
        old_baseline,old_target,old_owned_version=self._baseline,self._target,self._owned_version
        attempted=False
        def before_configuration():
            nonlocal attempted
            p.guard();attempted=True
        try:
            p.write(target,before_configuration,version)
        except Exception as write_error:
            if not attempted:raise
            # No blind retry/rollback: read the actual persisted configuration.
            try:
                observed,read_version=p.stable()
                if observed==before and read_version==version:
                    self._baseline,self._target,self._owned_version=old_baseline,old_target,old_owned_version
                    return self._report(before,version,verified=True,changed=False,operationError=str(write_error)) | {'ok':False,'reason':'write_not_applied'}
                # A completed commit whose receipt was lost is verified here.
                if observed!=target or read_version!=version:raise ValueError('foreign_or_partial_readback')
            except Exception:
                self._uncertain=True
                if self._baseline is None:self._baseline=before;self._owned_version=version
                self._target=target
                raise ValueError('write_outcome_unknown') from write_error
        try:
            observed,read_version=p.stable()
            if observed!=target or read_version!=version:raise ValueError('readback_mismatch')
        except Exception:
            self._uncertain=True
            if self._baseline is None:self._baseline=before;self._owned_version=version
            self._target=target
            raise ValueError('write_outcome_unknown')
        if restore:self._baseline=None;self._target=None;self._owned_version=None
        else:
            self._baseline=old_baseline if old_baseline is not None else before;self._target=target
            self._owned_version=old_owned_version if old_baseline is not None else version
        return self._report(observed,version,verified=True,changed=True)

    def _change(self,request,restore):
        if not self._lock.acquire(False):return self._failure(ValueError('busy'))
        try:return self._request(request,restore)
        except Exception as error:
            self._token=None
            return self._failure(error)
        finally:self._lock.release()

    def apply(self,request):return self._change(request,False)
    def restore(self,request):return self._change(request,True)

    def dispose(self):
        with self._lock:
            had_baseline=self._baseline is not None
            self._disposed=True;self._drop()
            return {'ok':True,'restored':False,'hadOwnedBaseline':had_baseline,
                    'recoveryRequired':self._uncertain}
