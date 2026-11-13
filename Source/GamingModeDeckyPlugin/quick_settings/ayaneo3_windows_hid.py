"""Native HID-class transport for the AYANEO 3 vendor collection.

No custom driver, elevation, input injection, mode switch or EC access.
Importing this file does not enumerate/open hardware. All WinAPI calls are lazy.
"""
import ctypes as C
from ctypes import wintypes as W
import hashlib
import json
import os
import sys
import threading
import time


class HidUnavailable(RuntimeError):
    pass


def _pending_operations():
    # Process-rooted across plugin/module unload; self-cycles can be GC-collected.
    pool=sys.__dict__.setdefault('_playhub_ayaneo3_pending_hid_v1',{})
    if type(pool) is not dict:raise HidUnavailable('quarantine_state_invalid')
    return pool


class GUID(C.Structure):
    _fields_ = [('a', W.DWORD), ('b', W.WORD), ('c', W.WORD), ('d', C.c_ubyte * 8)]


class Interface(C.Structure):
    _fields_ = [('cbSize', W.DWORD), ('guid', GUID), ('flags', W.DWORD), ('reserved', C.c_size_t)]


class DeviceInfo(C.Structure):
    _fields_ = [('cbSize', W.DWORD), ('guid', GUID), ('devinst', W.DWORD), ('reserved', C.c_size_t)]


class Attributes(C.Structure):
    _fields_ = [('Size', W.DWORD), ('VendorID', W.WORD), ('ProductID', W.WORD), ('VersionNumber', W.WORD)]


class Caps(C.Structure):
    _fields_ = [('Usage', W.WORD), ('UsagePage', W.WORD), ('InputReportByteLength', W.WORD),
               ('OutputReportByteLength', W.WORD), ('FeatureReportByteLength', W.WORD),
               ('Reserved', W.WORD * 17), ('Counts', W.WORD * 10)]


class Overlapped(C.Structure):
    _fields_ = [('Internal', C.c_size_t), ('InternalHigh', C.c_size_t),
               ('Offset', W.DWORD), ('OffsetHigh', W.DWORD), ('hEvent', W.HANDLE)]


def read_identity():
    import winreg
    with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r'HARDWARE\DESCRIPTION\System\BIOS') as key:
        manufacturer = winreg.QueryValueEx(key, 'SystemManufacturer')[0]
        model = winreg.QueryValueEx(key, 'SystemProductName')[0]
    if type(manufacturer) is not str or type(model) is not str:
        raise HidUnavailable('identity_unavailable')
    return manufacturer.strip().upper(), model.strip().upper()


def _api():
    if os.name != 'nt': raise HidUnavailable('windows_required')
    k = C.WinDLL('kernel32', use_last_error=True)
    h = C.WinDLL('hid', use_last_error=True)
    s = C.WinDLL('setupapi', use_last_error=True)
    m = C.WinDLL('cfgmgr32', use_last_error=True)
    def bind(dll, name, result, args):
        f = getattr(dll, name); f.restype = result; f.argtypes = args
    bind(k,'CreateFileW',W.HANDLE,[W.LPCWSTR,W.DWORD,W.DWORD,C.c_void_p,W.DWORD,W.DWORD,W.HANDLE])
    bind(k,'CloseHandle',W.BOOL,[W.HANDLE])
    bind(k,'CreateEventW',W.HANDLE,[C.c_void_p,W.BOOL,W.BOOL,W.LPCWSTR])
    bind(k,'ReadFile',W.BOOL,[W.HANDLE,C.c_void_p,W.DWORD,C.POINTER(W.DWORD),C.POINTER(Overlapped)])
    bind(k,'WriteFile',W.BOOL,[W.HANDLE,C.c_void_p,W.DWORD,C.POINTER(W.DWORD),C.POINTER(Overlapped)])
    bind(k,'WaitForSingleObject',W.DWORD,[W.HANDLE,W.DWORD])
    bind(k,'GetOverlappedResult',W.BOOL,[W.HANDLE,C.POINTER(Overlapped),C.POINTER(W.DWORD),W.BOOL])
    bind(k,'CancelIoEx',W.BOOL,[W.HANDLE,C.POINTER(Overlapped)])
    bind(h,'HidD_GetHidGuid',None,[C.POINTER(GUID)])
    bind(h,'HidD_GetAttributes',C.c_ubyte,[W.HANDLE,C.POINTER(Attributes)])
    bind(h,'HidD_GetPreparsedData',C.c_ubyte,[W.HANDLE,C.POINTER(C.c_void_p)])
    bind(h,'HidD_FreePreparsedData',C.c_ubyte,[C.c_void_p])
    bind(h,'HidP_GetCaps',C.c_int32,[C.c_void_p,C.POINTER(Caps)])
    bind(h,'HidD_FlushQueue',C.c_ubyte,[W.HANDLE])
    bind(s,'SetupDiGetClassDevsW',W.HANDLE,[C.POINTER(GUID),W.LPCWSTR,W.HWND,W.DWORD])
    bind(s,'SetupDiEnumDeviceInterfaces',W.BOOL,[W.HANDLE,C.c_void_p,C.POINTER(GUID),W.DWORD,C.POINTER(Interface)])
    bind(s,'SetupDiGetDeviceInterfaceDetailW',W.BOOL,[W.HANDLE,C.POINTER(Interface),C.c_void_p,W.DWORD,C.POINTER(W.DWORD),C.POINTER(DeviceInfo)])
    bind(s,'SetupDiDestroyDeviceInfoList',W.BOOL,[W.HANDLE])
    bind(m,'CM_Get_Parent',W.DWORD,[C.POINTER(W.DWORD),W.DWORD,W.DWORD])
    bind(m,'CM_Get_Device_IDW',W.DWORD,[W.DWORD,W.LPWSTR,W.ULONG,W.ULONG])
    bind(m,'CM_Get_DevNode_Status',W.DWORD,[C.POINTER(W.ULONG),C.POINTER(W.ULONG),W.DWORD,W.ULONG])
    return k,h,s,m


class WindowsHid:
    def __init__(self, api=None, identity=None, clock=None):
        self.k,self.h,self.s,self.m = api if api is not None else _api()
        self.identity = identity or read_identity
        self.clock = clock or time.monotonic
        self._quarantined = False
        self._pending_storage = None

    def _chain(self, devinst):
        status, problem = W.ULONG(), W.ULONG()
        if self.m.CM_Get_DevNode_Status(C.byref(status),C.byref(problem),devinst,0) != 0 or problem.value != 0 or not status.value & 8:
            raise HidUnavailable('device_not_current')
        chain=[]; current=devinst
        for _ in range(10):
            text=C.create_unicode_buffer(512)
            if self.m.CM_Get_Device_IDW(current,text,512,0) != 0:
                raise HidUnavailable('parent_identity_unavailable')
            item=text.value.upper()
            if not item or item in chain: raise HidUnavailable('parent_identity_invalid')
            chain.append(item)
            parent=W.DWORD()
            result=self.m.CM_Get_Parent(C.byref(parent),current,0)
            if result == 0xD: break  # CR_NO_SUCH_DEVNODE: reached root.
            if result != 0: raise HidUnavailable('parent_identity_unavailable')
            current=parent.value
        else: raise HidUnavailable('parent_chain_too_deep')
        if (len(chain)<3 or not chain[0].startswith('HID\\VID_1C4F&PID_0002')
                or not any(x.startswith('USB\\VID_1C4F&PID_0002') for x in chain[1:])):
            raise HidUnavailable('usb_parent_not_bound')
        return tuple(chain)

    def _started(self,devinst):
        status,problem=W.ULONG(),W.ULONG()
        return (self.m.CM_Get_DevNode_Status(C.byref(status),C.byref(problem),devinst,0)==0
                and problem.value==0 and bool(status.value&8))

    def _caps(self, handle):
        attrs=Attributes();attrs.Size=C.sizeof(attrs)
        if not self.h.HidD_GetAttributes(handle,C.byref(attrs)):
            raise HidUnavailable('hid_attributes_unavailable')
        preparsed=C.c_void_p()
        if not self.h.HidD_GetPreparsedData(handle,C.byref(preparsed)):
            raise HidUnavailable('hid_caps_unavailable')
        try:
            caps=Caps()
            if self.h.HidP_GetCaps(preparsed,C.byref(caps)) != 0x110000:
                raise HidUnavailable('hid_caps_unavailable')
            result=(attrs.VendorID,attrs.ProductID,attrs.VersionNumber,caps.UsagePage,caps.Usage,
                    caps.InputReportByteLength,caps.OutputReportByteLength)
        finally:
            self.h.HidD_FreePreparsedData(preparsed)
        if result[:2] != (0x1c4f,2) or result[3:] != (0xff00,1,65,65):
            raise HidUnavailable('not_ayaneo_vendor_collection')
        return result

    def open(self):
        if self._quarantined or _pending_operations(): raise HidUnavailable('prior_io_pending')
        identity=self.identity()
        if identity != ('AYANEO','AYANEO 3'): raise HidUnavailable('unsupported_device')
        guid=GUID(); self.h.HidD_GetHidGuid(C.byref(guid))
        infos=self.s.SetupDiGetClassDevsW(C.byref(guid),None,None,0x12)
        if infos in (None,0,C.c_void_p(-1).value): raise HidUnavailable('enumeration_unavailable')
        matches=[]; started=self.clock()
        try:
            for index in range(128):
                if self.clock()-started>1.5: raise HidUnavailable('enumeration_expired')
                row=Interface();row.cbSize=C.sizeof(row)
                if not self.s.SetupDiEnumDeviceInterfaces(infos,None,C.byref(guid),index,C.byref(row)):
                    if C.get_last_error()==259: break
                    raise HidUnavailable('enumeration_incomplete')
                size=W.DWORD(); info=DeviceInfo(); info.cbSize=C.sizeof(info)
                self.s.SetupDiGetDeviceInterfaceDetailW(infos,C.byref(row),None,0,C.byref(size),None)
                if not 8<=size.value<=4096: raise HidUnavailable('detail_length_invalid')
                data=C.create_string_buffer(size.value)
                C.cast(data,C.POINTER(W.DWORD))[0]=8 if C.sizeof(C.c_void_p)==8 else 6
                if not self.s.SetupDiGetDeviceInterfaceDetailW(infos,C.byref(row),data,size.value,None,C.byref(info)):
                    raise HidUnavailable('detail_unavailable')
                path=C.wstring_at(C.addressof(data)+4)
                if 'vid_1c4f&pid_0002' not in path.casefold(): continue
                handle=self.k.CreateFileW(path,0,3,None,3,0,None)
                if handle in (None,0,C.c_void_p(-1).value): raise HidUnavailable('candidate_unavailable')
                try:
                    try: caps=self._caps(handle)
                    except HidUnavailable as error:
                        if str(error)=='not_ayaneo_vendor_collection':continue
                        raise
                    chain=self._chain(info.devinst)
                    matches.append((path,info.devinst,caps,chain))
                finally: self.k.CloseHandle(handle)
                if self.clock()-started>1.5: raise HidUnavailable('enumeration_expired')
            else: raise HidUnavailable('enumeration_limit')
        finally: self.s.SetupDiDestroyDeviceInfoList(infos)
        if len(matches)!=1: raise HidUnavailable('missing_or_ambiguous_collection')
        path,devinst,caps,chain=matches[0]
        # Exclusive vendor collection; never detach/replace an OEM driver's handle.
        handle=self.k.CreateFileW(path,0xc0000000,0,None,3,0x40000000,None)
        if handle in (None,0,C.c_void_p(-1).value): raise HidUnavailable('vendor_collection_busy')
        session=Session(self,handle,path,devinst,caps,chain,identity)
        if not session.current(): session.close(); raise HidUnavailable('device_changed')
        return session


class Session:
    def __init__(self, owner, handle, path, devinst, caps, chain, identity):
        self.owner,self.handle,self.path,self.devinst=owner,handle,path,devinst
        self.caps,self.chain,self.identity=caps,chain,identity
        self.closed=False; self.poisoned=False; self._pending=False
        self.binding=hashlib.sha256(json.dumps((path,devinst,caps,chain,identity),
            separators=(',',':')).encode()).hexdigest()

    def current(self):
        if self.closed or self.poisoned: return False
        try:
            return (self.owner.identity()==self.identity and self.owner._caps(self.handle)==self.caps
                    and self.owner._chain(self.devinst)==self.chain
                    and self.owner.identity()==self.identity and self.owner._started(self.devinst)
                    and not self.closed and not self.poisoned)
        except Exception: return False

    def _quarantine(self,event,operation,buffer,count):
        self.poisoned=True;self._pending=True;self.owner._quarantined=True
        # Store before scheduling: failure to start a reaper must not free memory
        # referenced by a pending kernel IRP or permit a fresh handle to proceed.
        self.owner._pending_storage=(self,event,operation,buffer,count,self.handle)
        pool=_pending_operations();key=id(operation)
        pool[key]=self.owner._pending_storage
        def drain():
            completed=self.owner.k.GetOverlappedResult(self.handle,C.byref(operation),C.byref(count),True)
            if not completed and C.get_last_error()!=995:return
            self.owner.k.CloseHandle(event);self.owner.k.CloseHandle(self.handle)
            self.closed=True;self._pending=False
            self.owner._pending_storage=None;self.owner._quarantined=False
            pool.pop(key,None)
        try:threading.Thread(target=drain,daemon=True).start()
        except Exception:pass  # Quarantine retains the pending operation, no reuse.

    def _io(self, write, data, milliseconds, before_write=None):
        k=self.owner.k; event=k.CreateEventW(None,True,False,None)
        if not event: raise HidUnavailable('io_event_unavailable')
        operation=Overlapped();operation.hEvent=event
        buffer=C.create_string_buffer(data,65) if write else C.create_string_buffer(65)
        count=W.DWORD(); pending=False
        try:
            method=k.WriteFile if write else k.ReadFile
            if write and not self.current():raise HidUnavailable('device_changed_before_write')
            if write and before_write is not None: before_write()
            done=method(self.handle,buffer,65,C.byref(count),C.byref(operation))
            if not done:
                if C.get_last_error()!=997: raise HidUnavailable('hid_io_failed')
                pending=True
                if k.WaitForSingleObject(event,milliseconds)!=0:
                    raise HidUnavailable('hid_io_timeout')
                if not k.GetOverlappedResult(self.handle,C.byref(operation),C.byref(count),False):
                    if C.get_last_error()!=996:pending=False
                    raise HidUnavailable('hid_io_failed')
                pending=False
            if count.value!=65: raise HidUnavailable('short_hid_report')
            return buffer.raw if not write else None
        finally:
            if pending:
                k.CancelIoEx(self.handle,C.byref(operation))
                if not k.GetOverlappedResult(self.handle,C.byref(operation),C.byref(count),False) and C.get_last_error()==996:
                    self._quarantine(event,operation,buffer,count);event=None
            if event: k.CloseHandle(event)

    def exchange(self, command, before_write=None):
        if not safe_command(command):
            raise HidUnavailable('command_shape_invalid')
        if not self.current(): raise HidUnavailable('device_not_current')
        if not self.owner.h.HidD_FlushQueue(self.handle): raise HidUnavailable('queue_flush_failed')
        deadline=self.owner.clock()+0.4
        self._io(True,command,400,before_write)
        while self.owner.clock()<deadline:
            raw=self._io(False,None,max(1,int((deadline-self.owner.clock())*1000)))
            if raw[0]!=0: raise HidUnavailable('unexpected_report_id')
            response=raw[1:]
            if response[3]==command[4]:
                if not self.current(): raise HidUnavailable('device_changed')
                return response
        raise HidUnavailable('controller_response_timeout')

    def close(self):
        if not self.closed and not self._pending:
            self.closed=True; self.owner.k.CloseHandle(self.handle)


def safe_command(command):
    if type(command) is not bytes or len(command)!=65 or command[0]!=0: return False
    check=bytes((0,0,0,0,8))+bytes(60)
    if command==check: return True
    template=bytearray(65);template[3:5]=b'\x21\x09'
    template[22:24]=b'\x33\x22';template[32]=1;template[37:39]=b'\x64\x64'
    for i in (1,2,*range(8,16),24): template[i]=command[i]
    return (bytes(template)==command and command[8] in (1,2,3,255)
            and command[12] in (1,2,3,255) and command[24] in (16,32,48,64)
            and int.from_bytes(command[1:3],'little')==sum(command[7:]))
