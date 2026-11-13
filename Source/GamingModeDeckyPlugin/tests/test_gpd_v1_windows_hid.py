"""Actual Windows transport fake tests; native seam adapted from prior Playhub AYANEO tests."""
import ctypes as C
import gc
import weakref
from ctypes import wintypes as W
import importlib.util
import pathlib
import sys
import types
import unittest
from unittest.mock import patch

SOURCE = pathlib.Path(__file__).resolve().parents[1]/'quick_settings/gpd_v1_windows_hid.py'
spec=importlib.util.spec_from_file_location('actual_gpd_native_checks',SOURCE)
N=importlib.util.module_from_spec(spec);spec.loader.exec_module(N)
IDENTITY=('GPD','G1618-04')
CHECK=bytes((1,0xa5,0x10,0x5a,0xef))+bytes(27)

def put(pointer, kind, value): C.cast(pointer,C.POINTER(kind))[0]=value

class Kernel:
    def __init__(self):
        self.closed=[];self.created=[];self.writes=[];self.cancelled=[]
        self.count=64;self.payload=bytes(range(64));self.pending=False;self.wait=0
        self.results=[];self.event=200
    def CloseHandle(self,h): self.closed.append(h);return True
    def CreateEventW(self,*_): self.event+=1;self.created.append(self.event);return self.event
    def DeviceIoControl(self,h,code,inp,insz,out,outsz,c,o):
        if code==0xb0195:
            self.writes.append(bytes(C.string_at(inp,insz)));put(c,W.DWORD,0)
        elif code==0xb01a2:
            assert insz==outsz==64 and C.string_at(inp,1)==b'\x01'
            C.memmove(out,self.payload,min(outsz,len(self.payload)));put(c,W.DWORD,self.count)
        else:raise AssertionError('unexpected IOCTL')
        if self.pending:C.set_last_error(997);return False
        return True
    def WaitForSingleObject(self,*_):return self.wait
    def CancelIoEx(self,h,o):self.cancelled.append(h);return True
    def GetOverlappedResult(self,h,o,c,wait):
        good,error,count=self.results.pop(0) if self.results else (True,0,self.count)
        put(c,W.DWORD,count);C.set_last_error(error);return good
    def CreateFileW(self,path,access,share,security,disposition,flags,template):
        self.opened=getattr(self,'opened',[])+[(path,access,share,flags)]
        return 80 if access else 40

class Hid:
    def __init__(self):self.freed=[];self.caps=(0xff00,1,65,33);self.flush=lambda:True
    def HidD_GetAttributes(self,h,p):
        value=C.cast(p,C.POINTER(N.Attributes)).contents
        value.VendorID=0x2f24;value.ProductID=0x135;value.VersionNumber=9;return True
    def HidD_GetPreparsedData(self,h,p):put(p,C.c_void_p,300);return True
    def HidP_GetCaps(self,p,c):
        value=C.cast(c,C.POINTER(N.Caps)).contents
        value.UsagePage,value.Usage,value.InputReportByteLength,value.OutputReportByteLength=self.caps
        return 0x110000
    def HidD_FreePreparsedData(self,p):self.freed.append(p);return True
    def HidD_FlushQueue(self,h):return self.flush()
    def HidD_GetHidGuid(self,p):pass

class Setup:
    def __init__(self,paths):self.paths=paths;self.destroyed=[]
    def SetupDiGetClassDevsW(self,*args):return 60
    def SetupDiEnumDeviceInterfaces(self,infos,parent,guid,index,row):
        if index>=len(self.paths):C.set_last_error(259);return False
        C.cast(row,C.POINTER(N.Interface)).contents.flags=index;return True
    def SetupDiGetDeviceInterfaceDetailW(self,infos,row,data,size,needed,info):
        if not data:put(needed,W.DWORD,512);C.set_last_error(122);return False
        index=C.cast(row,C.POINTER(N.Interface)).contents.flags
        text=C.create_unicode_buffer(self.paths[index]);C.memmove(C.addressof(data)+4,text,C.sizeof(text))
        C.cast(info,C.POINTER(N.DeviceInfo)).contents.devinst=3;return True
    def SetupDiDestroyDeviceInfoList(self,h):self.destroyed.append(h);return True

class Cm:
    def __init__(self):
        self.ids={3:r'HID\VID_2F24&PID_0135\COL01',2:r'USB\VID_2F24&PID_0135\PHYSICAL',1:r'ROOT\SYSTEM\0000'}
        self.started=True;self.after_id=None
    def CM_Get_DevNode_Status(self,status,problem,devinst,flags):
        put(status,W.ULONG,8 if self.started else 0);put(problem,W.ULONG,0);return 0
    def CM_Get_Device_IDW(self,devinst,buffer,n,flags):
        buffer.value=self.ids[devinst]
        if self.after_id:self.after_id(devinst)
        return 0
    def CM_Get_Parent(self,parent,devinst,flags):
        if devinst==1:return 0xd
        put(parent,W.DWORD,devinst-1);return 0

def session():
    k,h,m=Kernel(),Hid(),Cm();ident=[IDENTITY]
    owner=N.WindowsHid(api=(k,h,types.SimpleNamespace(),m),identity=lambda:ident[0],clock=lambda:1.)
    caps=owner._caps(40);chain=owner._chain(3)
    return N.Session(owner,40,'test-path',3,caps,chain,IDENTITY),k,h,m,ident


class Tests(unittest.TestCase):
    def tearDown(self):
        # These are fake IRPs only. Never used by a production get/command.
        N._pending_operations().clear()
    def test_boolean_abi(self):
        class Dll:
            def __init__(self):self.f={}
            def __getattr__(self,name):return self.f.setdefault(name,types.SimpleNamespace())
        dlls={name:Dll()for name in('kernel32','hid','setupapi','cfgmgr32')}
        with patch.object(N.C,'WinDLL',lambda name,**kwargs:dlls[name]):N._api()
        for name in('HidD_GetAttributes','HidD_GetPreparsedData','HidD_FreePreparsedData','HidD_FlushQueue'):
            self.assertIs(dlls['hid'].f[name].restype,C.c_ubyte)
        self.assertEqual(len(dlls['kernel32'].f['DeviceIoControl'].argtypes),8)
    def test_unique_exclusive_open(self):
        s,k,h,m,ident=session();setup=Setup([r'\\?\hid#vid_2f24&pid_0135#internal'])
        owner=N.WindowsHid(api=(k,h,setup,m),identity=lambda:IDENTITY,clock=lambda:1.)
        retained=owner.open();self.assertTrue(retained.current());self.assertEqual(setup.destroyed,[60])
        self.assertEqual([(x[1],x[2],x[3])for x in k.opened],[(0,3,0),(0xc0000000,0,0x40000000)])
        retained.close();self.assertEqual(k.closed,[40,80])
    def test_duplicate_never_opens_write(self):
        s,k,h,m,ident=session();setup=Setup([r'\\?\hid#vid_2f24&pid_0135#a',r'\\?\hid#vid_2f24&pid_0135#b'])
        owner=N.WindowsHid(api=(k,h,setup,m),identity=lambda:IDENTITY,clock=lambda:1.)
        with self.assertRaisesRegex(N.HidUnavailable,'missing_or_ambiguous'):owner.open()
        self.assertEqual([x[1]for x in k.opened],[0,0])
    def test_desktop_manual_profile_cannot_enumerate(self):
        s,k,h,m,ident=session();owner=N.WindowsHid(api=(k,h,object(),m),identity=lambda:('GPD','DESKTOP'))
        with self.assertRaisesRegex(N.HidUnavailable,'unsupported_device'):owner.open()
        self.assertFalse(getattr(k,'opened',[]))
    def test_mini2025_no_enumeration(self):
        s,k,h,m,ident=session();owner=N.WindowsHid(api=(k,h,object(),m),identity=lambda:('GPD','G1617-02'))
        with self.assertRaisesRegex(N.HidUnavailable,'unsupported_device'):owner.open()
    def test_caps_reject_unknown_shape(self):
        for wrong in[(0xff01,1,65,33),(0xff00,0,65,33),(0xff00,1,63,33),(0xff00,1,65,34)]:
            s,k,h,m,ident=session();h.caps=wrong
            with self.assertRaises(N.HidUnavailable):s.owner._caps(40)
            self.assertEqual(len(h.freed),2)
    def test_caps_native_layout(self):
        self.assertEqual(C.sizeof(N.Caps),64);self.assertEqual(C.sizeof(N.Overlapped),32)
        self.assertEqual(C.sizeof(N.Interface),32);self.assertEqual(C.sizeof(N.DeviceInfo),32)
    def test_parent_chain_and_late_started(self):
        s,k,h,m,ident=session();self.assertTrue(s.current());m.after_id=lambda devinst:setattr(m,'started',False)if devinst==1 else None
        self.assertFalse(s.current())
    def test_late_dmi_rejected(self):
        s,k,h,m,ident=session();m.after_id=lambda devinst:ident.__setitem__(0,('GPD','G1617-02'))if devinst==1 else None
        self.assertFalse(s.current())
    def test_pid_prefix_collision_rejected(self):
        s,k,h,m,ident=session();m.ids[2]=r'USB\VID_2F24&PID_01350\OTHER';self.assertFalse(s.current())
    def test_parent_usb_mismatch(self):
        s,k,h,m,ident=session();m.ids[2]=r'USB\VID_2F24&PID_0137\FOREIGN';self.assertFalse(s.current())
    def test_output_zero_padding_no_extra_fallback(self):
        s,k,h,m,ident=session();s._io(True,CHECK);self.assertEqual(k.writes,[CHECK+b'\0']);self.assertEqual(k.closed,[201])
    def test_input_exact_raw64_not_stripped(self):
        s,k,h,m,ident=session();self.assertEqual(s._io(False,None),bytes(range(64)));self.assertEqual(k.closed,[201])
    def test_short_input_rejected(self):
        s,k,h,m,ident=session();k.count=63
        with self.assertRaisesRegex(N.HidUnavailable,'short_hid_report'):s._io(False,None)
        self.assertEqual(k.closed,[201])
    def test_pending_completion_uses_actual_count(self):
        s,k,h,m,ident=session();k.pending=True;k.results=[(True,0,64)]
        self.assertEqual(s._io(False,None),bytes(range(64)))
    def test_cancel_completed_keeps_safe_lifetime(self):
        s,k,h,m,ident=session();k.pending=True;k.wait=258;k.results=[(False,995,0)]
        with self.assertRaisesRegex(N.HidUnavailable,'hid_io_timeout'):s._io(False,None)
        self.assertEqual(k.cancelled,[40]);self.assertEqual(k.closed,[201]);self.assertFalse(s._pending)
    def test_incomplete_cancel_and_thread_failure_quarantines(self):
        s,k,h,m,ident=session();k.pending=True;k.wait=258;k.results=[(False,996,0)]
        class T:
            def __init__(self,**kw):pass
            def start(self):raise RuntimeError('thread limit')
        with patch.object(N.threading,'Thread',T):
            with self.assertRaisesRegex(N.HidUnavailable,'hid_io_timeout'):s._io(False,None)
        self.assertTrue(s.owner._quarantined);self.assertIsNotNone(s.owner._pending_storage);self.assertFalse(k.closed)
        s.close();self.assertFalse(k.closed)
        owner_ref,session_ref,buffer_ref=weakref.ref(s.owner),weakref.ref(s),weakref.ref(s.owner._pending_storage[3])
        del s;gc.collect()
        self.assertIsNotNone(owner_ref());self.assertIsNotNone(session_ref());self.assertIsNotNone(buffer_ref())
        fresh=N.WindowsHid(api=(k,h,object(),m),identity=lambda:IDENTITY)
        with self.assertRaisesRegex(N.HidUnavailable,'prior_io_pending'):fresh.open()
    def test_drain_unknown_keeps_memory_but_confirmed_abort_releases(self):
        s,k,h,m,ident=session();k.pending=True;k.wait=258;k.results=[(False,996,0),(False,6,0)]
        threads=[]
        class T:
            def __init__(self,target,daemon):self.target=target;threads.append(self)
            def start(self):pass
        with patch.object(N.threading,'Thread',T):
            with self.assertRaises(N.HidUnavailable):s._io(False,None)
        self.assertTrue(N._pending_operations());threads[0].target()
        self.assertTrue(N._pending_operations());self.assertFalse(k.closed)
        k.results=[(False,995,0)];threads[0].target()
        self.assertFalse(N._pending_operations());self.assertEqual(k.closed,[201,40]);self.assertTrue(s.closed)
    def test_drift_in_createevent_prevents_configuration(self):
        s,k,h,m,ident=session();old=k.CreateEventW
        def event(*args):m.started=False;return old(*args)
        k.CreateEventW=event
        with self.assertRaises(N.HidUnavailable):s._io(True,CHECK)
        self.assertFalse(k.writes)
    def test_copy_fence_last_before_ioctl(self):
        s,k,h,m,ident=session()
        def guard():raise ValueError('fresh operation expired')
        with self.assertRaisesRegex(ValueError,'expired'):s._io(True,CHECK,guard)
        self.assertFalse(k.writes);self.assertEqual(k.closed,[201])
    def test_exchange_reply_contract_and_no_fallback(self):
        s,k,h,m,ident=session()
        with self.assertRaisesRegex(N.HidUnavailable,'command_shape_invalid'):s.exchange(CHECK,reply=False)
        self.assertFalse(k.writes)
        with patch.object(N.time,'sleep',lambda _:None):self.assertEqual(s.exchange(CHECK),bytes(range(64)))
        self.assertEqual(k.writes,[CHECK+b'\0'])
if __name__=='__main__':unittest.main(verbosity=2)
