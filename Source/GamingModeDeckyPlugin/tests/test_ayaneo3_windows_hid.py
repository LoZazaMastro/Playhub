"""Independent actual Windows transport tests. All native calls are fakes."""
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

SOURCE = pathlib.Path(__file__).resolve().parents[1] / 'quick_settings' / 'ayaneo3_windows_hid.py'
spec=importlib.util.spec_from_file_location('actual_ayaneo_native_peer',SOURCE)
N=importlib.util.module_from_spec(spec);spec.loader.exec_module(N)
IDENTITY=('AYANEO','AYANEO 3')
CHECK=bytes((0,0,0,0,8))+bytes(60)

def put(pointer, kind, value): C.cast(pointer,C.POINTER(kind))[0]=value

class Kernel:
    def __init__(self):
        self.closed=[];self.created=[];self.writes=[];self.cancelled=[]
        self.count=65;self.payload=b'\0'*65;self.pending=False;self.wait=0
        self.results=[];self.event=200
    def CloseHandle(self,h): self.closed.append(h);return True
    def CreateEventW(self,*_): self.event+=1;self.created.append(self.event);return self.event
    def WriteFile(self,h,b,n,c,o):
        self.writes.append(bytes(C.string_at(b,n)));put(c,W.DWORD,self.count)
        if self.pending: C.set_last_error(997);return False
        return True
    def ReadFile(self,h,b,n,c,o):
        C.memmove(b,self.payload,min(n,len(self.payload)));put(c,W.DWORD,self.count)
        if self.pending: C.set_last_error(997);return False
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
    def __init__(self):self.freed=[];self.caps=(0xff00,1,65,65);self.flush=lambda:True
    def HidD_GetAttributes(self,h,p):
        value=C.cast(p,C.POINTER(N.Attributes)).contents
        value.VendorID=0x1c4f;value.ProductID=2;value.VersionNumber=9;return True
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
        self.ids={3:r'HID\VID_1C4F&PID_0002\COL01',2:r'USB\VID_1C4F&PID_0002\PHYSICAL',1:r'ROOT\SYSTEM\0000'}
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

class NativeTests(unittest.TestCase):
    def tearDown(self):
        # All operations in this test file are fake IRPs, never device requests.
        N._pending_operations().clear()
    def test_hidd_bindings_use_eight_bit_boolean_abi(self):
        class Dll:
            def __init__(self):self.f={}
            def __getattr__(self,name):return self.f.setdefault(name,types.SimpleNamespace())
        dlls={name:Dll() for name in ('kernel32','hid','setupapi','cfgmgr32')}
        with patch.object(N.C,'WinDLL',lambda name,**kwargs:dlls[name]):N._api()
        for name in ('HidD_GetAttributes','HidD_GetPreparsedData','HidD_FreePreparsedData','HidD_FlushQueue'):
            self.assertIs(dlls['hid'].f[name].restype,C.c_ubyte,name+' is BOOLEAN, not Win32 BOOL')
    def test_enumeration_retains_only_unique_exclusive_candidate(self):
        s,k,h,m,ident=session();setup=Setup([r'\\?\hid#vid_1c4f&pid_0002#internal'])
        owner=N.WindowsHid(api=(k,h,setup,m),identity=lambda:IDENTITY,clock=lambda:1.)
        retained=owner.open();self.assertTrue(retained.current());self.assertEqual(setup.destroyed,[60])
        self.assertEqual([(x[1],x[2],x[3]) for x in k.opened],[(0,3,0),(0xc0000000,0,0x40000000)])
        self.assertEqual(k.closed,[40]);retained.close();retained.close();self.assertEqual(k.closed,[40,80])
    def test_ambiguous_collection_never_opens_write_handle(self):
        s,k,h,m,ident=session();setup=Setup([r'\\?\hid#vid_1c4f&pid_0002#a',r'\\?\hid#vid_1c4f&pid_0002#b'])
        owner=N.WindowsHid(api=(k,h,setup,m),identity=lambda:IDENTITY,clock=lambda:1.)
        with self.assertRaisesRegex(N.HidUnavailable,'missing_or_ambiguous'):owner.open()
        self.assertEqual([x[1] for x in k.opened],[0,0]);self.assertEqual(setup.destroyed,[60])
    def test_native_layouts(self):
        self.assertEqual(C.sizeof(N.Caps),64)
        self.assertEqual(N.Caps.Reserved.offset,10)
        self.assertEqual(N.Caps.Counts.offset,44)
        self.assertEqual(C.sizeof(N.Overlapped),32)
        self.assertEqual(C.sizeof(N.Interface),32)
        self.assertEqual(C.sizeof(N.DeviceInfo),32)
    def test_caps_require_input_output_65_and_vendor_usage(self):
        for wrong in [(0xff01,1,65,65),(0xff00,2,65,65),(0xff00,1,64,65),(0xff00,1,65,64)]:
            s,k,h,m,ident=session();h.caps=wrong
            with self.assertRaises(N.HidUnavailable):s.owner._caps(40)
            self.assertEqual(len(h.freed),2)
    def test_current_positive_and_currentness_negatives(self):
        s,k,h,m,ident=session();self.assertTrue(s.current())
        ident[0]=('AYANEO','AYANEO 2');self.assertFalse(s.current())
        ident[0]=IDENTITY;m.started=False;self.assertFalse(s.current())
        m.started=True;m.ids[2]=r'USB\VID_0000&PID_0000\WRONG';self.assertFalse(s.current())
    def test_immediate_io_requires_exact_length_and_closes_event(self):
        s,k,h,m,ident=session();self.assertIsNone(s._io(True,CHECK,400));self.assertEqual(k.writes,[CHECK])
        self.assertEqual(k.closed,[201]);k.count=64
        with self.assertRaisesRegex(N.HidUnavailable,'short_hid_report'):s._io(False,None,400)
        self.assertEqual(k.closed,[201,202])
    def test_pending_success_obtains_actual_count(self):
        s,k,h,m,ident=session();k.pending=True;k.results=[(True,0,65)]
        self.assertEqual(s._io(False,None,400),bytes(65));self.assertEqual(k.closed,[201])
    def test_timeout_completed_cancellation_safe_close(self):
        s,k,h,m,ident=session();k.pending=True;k.wait=258;k.results=[(False,995,0)]
        with self.assertRaisesRegex(N.HidUnavailable,'hid_io_timeout'):s._io(False,None,400)
        self.assertEqual(k.cancelled,[40]);self.assertEqual(k.closed,[201]);self.assertFalse(s._pending)
    def test_pending_drain_retains_and_later_closes_exact_buffers(self):
        s,k,h,m,ident=session();k.pending=True;k.wait=258;k.results=[(False,996,0),(False,995,0)]
        threads=[]
        class T:
            def __init__(self,target,daemon):self.target=target;threads.append(self)
            def start(self):pass
        with patch.object(N.threading,'Thread',T):
            with self.assertRaisesRegex(N.HidUnavailable,'hid_io_timeout'):s._io(False,None,400)
        self.assertTrue(s._pending);self.assertTrue(s.owner._quarantined);self.assertEqual(k.closed,[])
        s.close();self.assertEqual(k.closed,[]);threads[0].target()
        self.assertEqual(k.closed,[201,40]);self.assertTrue(s.closed);self.assertFalse(s.owner._quarantined)
    def test_response_strips_zero_id_and_rejects_nonzero(self):
        s,k,h,m,ident=session();r=bytearray(65);r[4]=8;k.payload=bytes(r)
        self.assertEqual(s.exchange(CHECK),bytes(r[1:]));r[0]=1;k.payload=bytes(r)
        with self.assertRaisesRegex(N.HidUnavailable,'unexpected_report_id'):s.exchange(CHECK)
    def test_only_check_or_specific_configuration_allowed(self):
        self.assertTrue(N.safe_command(CHECK))
        for opcode in [5,10,0x1a]:
            v=bytearray(CHECK);v[4]=opcode;self.assertFalse(N.safe_command(bytes(v)))
        self.assertFalse(N.safe_command(CHECK[:-1]));self.assertFalse(N.safe_command(bytearray(CHECK)))

class BoundaryRepros(unittest.TestCase):
    def tearDown(self):
        # All operations in this test file are fake IRPs, never device requests.
        N._pending_operations().clear()
    def test_last_flush_device_loss_must_prevent_configuration_write(self):
        s,k,h,m,ident=session();command=bytearray(65);command[3:5]=b'\x21\x09'
        command[8]=command[12]=1;command[22:25]=bytes((0x33,0x22,0x20));command[32]=1;command[37:39]=b'\x64\x64'
        command[1:3]=sum(command[7:]).to_bytes(2,'little')
        reply=bytearray(65);reply[4]=9;k.payload=bytes(reply)
        h.flush=lambda:(setattr(m,'started',False) or True)
        with self.assertRaises(N.HidUnavailable):s.exchange(bytes(command))
        self.assertEqual(k.writes,[],'configuration was dispatched after collection stopped during flush')
    def test_final_parent_getter_stops_device(self):
        s,k,h,m,ident=session();m.after_id=lambda devinst:setattr(m,'started',False) if devinst==1 else None
        accepted=s.current()
        self.assertFalse(accepted,'device stopped during final parent getter, yet current() accepted it')
    def test_final_parent_getter_changes_identity(self):
        s,k,h,m,ident=session();m.after_id=lambda devinst:ident.__setitem__(0,('AYANEO','AYANEO 2')) if devinst==1 else None
        self.assertFalse(s.current(),'DMI changed in final parent getter, yet current() accepted it')
    def test_drain_unknown_keeps_memory_until_confirmed_abort(self):
        s,k,h,m,ident=session();k.pending=True;k.wait=258;k.results=[(False,996,0),(False,6,0)]
        threads=[]
        class T:
            def __init__(self,target,daemon):self.target=target;threads.append(self)
            def start(self):pass
        with patch.object(N.threading,'Thread',T):
            with self.assertRaises(N.HidUnavailable):s._io(False,None,400)
        threads[0].target();self.assertTrue(N._pending_operations());self.assertEqual(k.closed,[])
        k.results=[(False,995,0)];threads[0].target()
        self.assertFalse(N._pending_operations());self.assertEqual(k.closed,[201,40])
    def test_thread_start_failure_strong_gc_lifetime(self):
        s,k,h,m,ident=session();k.pending=True;k.wait=258;k.results=[(False,996,0)]
        class T:
            def __init__(self,**kw):pass
            def start(self):raise RuntimeError('thread limit')
        with patch.object(N.threading,'Thread',T):
            with self.assertRaises(N.HidUnavailable):s._io(False,None,400)
        owner,retained,buffer=weakref.ref(s.owner),weakref.ref(s),weakref.ref(s.owner._pending_storage[3])
        del s;gc.collect()
        self.assertIsNotNone(owner());self.assertIsNotNone(retained());self.assertIsNotNone(buffer())
        fresh=N.WindowsHid(api=(k,h,object(),m),identity=lambda:IDENTITY)
        with self.assertRaisesRegex(N.HidUnavailable,'prior_io_pending'):fresh.open()
    def test_thread_start_failure_must_not_release_pending_event(self):
        s,k,h,m,ident=session();k.pending=True;k.wait=258;k.results=[(False,996,0)]
        class T:
            def __init__(self,target,daemon):self.target=target
            def start(self):raise RuntimeError('simulated thread limit')
        with patch.object(N.threading,'Thread',T):
            with self.assertRaises(Exception):s._io(False,None,400)
        self.assertEqual(k.closed,[],'event/buffer lifetime must outlive pending IRP even if starting drain fails')

if __name__=='__main__':unittest.main(verbosity=2)
