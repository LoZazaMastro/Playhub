import ctypes,importlib.util,os,pathlib,sys,time,unittest
sys.stdout.reconfigure(encoding='utf-8')
ROOT=pathlib.Path(__file__).resolve().parent
path=pathlib.Path(os.environ.get('STORE_PIPE_SOURCE',ROOT.parent/'store_library.py'))
spec=importlib.util.spec_from_file_location('tested_store_pipe',path);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class Kernel:
    def __init__(self,pending=False,timeout=False,failed=False,zero=False):
        self.pending=pending;self.timeout=timeout;self.failed=failed;self.zero=zero;self.calls=[];self.payload=b'{"protocol":1}\n';self.transferred=0
    def CreateEventW(self,*a):self.calls.append('event');return 55
    def io(self,writing,handle,buffer,size,count,operation):
        self.calls.append('write'if writing else'read');self.transferred=size if writing else len(self.payload)
        if not writing:ctypes.memmove(buffer,self.payload,len(self.payload))
        # With FILE_FLAG_OVERLAPPED, the authoritative count is in completion state.
        # The optional synchronous count is not used as a completion receipt.
        if count is not None:count._obj.value=0
        operation._obj.internal_high=self.transferred
        if self.failed:ctypes.set_last_error(5);return 0
        if self.pending:ctypes.set_last_error(997);return 0
        return 1
    def ReadFile(self,*a):return self.io(False,*a)
    def WriteFile(self,*a):return self.io(True,*a)
    def WaitForSingleObject(self,*a):self.calls.append('wait');return 258 if self.timeout else 0
    def GetOverlappedResult(self,handle,operation,count,wait):
        self.calls.append('drain'if wait else'result')
        if wait and self.timeout:ctypes.set_last_error(995);return 0
        count._obj.value=0 if self.zero else self.transferred;return 1
    def CancelIoEx(self,*a):self.calls.append('cancel');return 1
    def CloseHandle(self,handle):self.calls.append('close-event'if handle==55 else'close-pipe');return 1

def pipe(kernel):p=m._Pipe.__new__(m._Pipe);p.kernel=kernel;p.handle=77;return p
class Checks(unittest.TestCase):
    def test_immediate_read_uses_authoritative_completed_count(self):
        k=Kernel();self.assertEqual(pipe(k).transfer(False,None,time.monotonic()+1),k.payload);self.assertEqual(k.calls,['event','read','result','close-event'])
    def test_immediate_write_uses_authoritative_completed_count(self):
        k=Kernel();self.assertEqual(pipe(k).transfer(True,b'write\n',time.monotonic()+1),6);self.assertEqual(k.calls,['event','write','result','close-event'])
    def test_pending_read_gets_completed_count_and_closes_event(self):
        k=Kernel(pending=True);self.assertEqual(pipe(k).transfer(False,None,time.monotonic()+1),k.payload);self.assertEqual(k.calls,['event','read','wait','result','close-event'])
    def test_pending_write_gets_completed_count_and_closes_event(self):
        k=Kernel(pending=True);self.assertEqual(pipe(k).transfer(True,b'abc',time.monotonic()+1),3);self.assertEqual(k.calls,['event','write','wait','result','close-event'])
    def test_timeout_cancels_exact_operation_drains_then_closes(self):
        k=Kernel(pending=True,timeout=True)
        with self.assertRaises(TimeoutError):pipe(k).transfer(False,None,time.monotonic()+.01)
        self.assertEqual(k.calls,['event','read','wait','cancel','drain','close-event'])
    def test_nonpending_io_error_closes_event_without_cancellation(self):
        k=Kernel(failed=True)
        with self.assertRaisesRegex(RuntimeError,'store_pipe_failed'):pipe(k).transfer(False,None,time.monotonic()+1)
        self.assertEqual(k.calls,['event','read','close-event'])
    def test_zero_byte_completion_is_not_fabricated(self):
        k=Kernel(zero=True);self.assertEqual(pipe(k).transfer(False,None,time.monotonic()+1),b'')
    def test_methods_remain_account_authorization_readonly(self):
        for method in ['connect-begin','connect-complete','get-token','execute']:
            with self.assertRaisesRegex(ValueError,'store_method_invalid'):m._rpc(method,{})
        for method in ['steam-sync-request','steam-sync-status','steam-sync-created','disconnect','directories','prepare-parent','ui-install-take','clear-canceled','remove-history','cancel-cleanup','windows-install-dismiss','steam-native-identities','steam-adopt-import','steam-forget-uninstalled','steam-repair-missing','steam-migration-pending','steam-migration-report','xbox-catalog','xbox-refresh','xbox-gamepass-install']:
            self.assertIn(method,m._ALLOWED)
        self.assertEqual(ctypes.sizeof(m._Overlapped),32 if ctypes.sizeof(ctypes.c_void_p)==8 else 20)
if __name__=='__main__':unittest.main()
