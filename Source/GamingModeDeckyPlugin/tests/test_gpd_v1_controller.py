from pathlib import Path
import importlib.util,unittest
R=Path(__file__).resolve().parents[1]
P=R/'quick_settings'
def load(name):
 s=importlib.util.spec_from_file_location(name,P/(name+'.py'));m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
M=load('gpd_v1_controller');N=load('gpd_v1_windows_hid')
class Timer:
 def __init__(self,delay,callback):self.callback=callback
 def start(self):pass
 def cancel(self):pass
class Session:
 def __init__(self,identity=('GPD','G1618-04'),firmware=(4,7)):
  self.identity=identity;self.firmware=firmware;self.xfirmware=(1,2);self.binding='retained USB parent chain + caps + path';self.closed=False
  self.data=bytes((i*17)%256 for i in range(128));self.data=self.data[:66]+b'\0'+self.data[67:]
  self.stage=bytearray(self.data);self.calls=[];self.fail=None;self.before=None;self.unstable=False;self.reads=0;self.badchecksum=False
 def current(self):return not self.closed
 def close(self):self.closed=True
 def exchange(self,raw,*,reply=True,before_write=None):
  assert N.safe_command(raw);assert reply==(raw[2]not in(0x21,0x23))
  if not self.current():raise ValueError('device_removed')
  code,page=raw[2],raw[6]
  if self.before:self.before(code,page)
  if before_write:before_write()
  self.calls.append((code,page,raw))
  fail=self.fail(code,page) if self.fail else None
  if fail=='before':raise OSError('actual transport refusal before effect')
  result=bytearray(64)
  if code in(0x10,0x20):
   result[8]=0xaa;result[9:13]=bytes((*self.xfirmware,*self.firmware))
   if code==0x20:self.stage=bytearray(self.data)
  elif code==0x11:
   result[:]=self.data[page*64:(page+1)*64]
   if page==0:self.reads+=1
   if self.unstable and self.reads%2:result[0]^=1
  elif code==0x12:result[24:28]=sum(self.data).to_bytes(4,'little')
  elif code==0x21:self.stage[page*16:(page+1)*16]=raw[8:24]
  elif code==0x22:result[24:28]=(sum(self.stage)+int(self.badchecksum)).to_bytes(4,'little')
  elif code==0x23:self.data=bytes(self.stage)
  if fail=='after':raise OSError('receipt lost after effect')
  if fail=='foreign':self.data=bytes((self.data[0]^1,))+self.data[1:]
  return bytes(result) if reply else None
class Transport:
 def __init__(self,s):self.s=s;self.opens=0
 def open(self):self.opens+=1;return self.s
class Tests(unittest.TestCase):
 def setup(self,identity=('GPD','G1618-04'),fw=(4,7)):
  self.s=Session(identity,fw);self.t=Transport(self.s);self.now=0
  self.c=M.GpdV1Controller(transport_factory=lambda:self.t,clock=lambda:self.now,timer_factory=Timer)
  self.baseline=self.s.data
 def get(self):
  x=self.c.get();self.assertTrue(x['ok'],x);return x
 def apply(self,rumble):return self.c.apply(dict(token=self.get()['token'],rumble=rumble))
 def writes(self):return[x for x in self.s.calls if x[0]in(0x21,0x23)]
 def test_no_query_on_construct(self):self.setup();self.assertEqual(self.t.opens,0)
 def test_initial_double_read_actual128(self):
  self.setup();r=self.get();self.assertEqual(self.s.reads,2);self.assertEqual(r['state'],{'rumble':'off'});self.assertFalse(r['hardwareValidated']);self.assertFalse(self.writes())
 def test_apply_apply_restore_original(self):
  self.setup();self.assertTrue(self.apply('low')['verified']);self.assertTrue(self.apply('high')['verified'])
  r=self.c.restore({'token':self.get()['token']});self.assertTrue(r['ok'],r);self.assertEqual(self.s.data,self.baseline);self.assertFalse(r['restoreAvailable'])
 def test_unknown_bytes_preserved(self):
  self.setup();self.assertTrue(self.apply('high')['ok']);self.assertEqual(self.s.data[:66]+self.s.data[67:],self.baseline[:66]+self.baseline[67:])
 def test_exact_8_pages_then_checksum_single_commit(self):
  self.setup();self.assertTrue(self.apply('high')['ok']);codes=[x[0] for x in self.s.calls];i=codes.index(0x20)
  self.assertEqual(codes[i:i+11],[0x20]+[0x21]*8+[0x22,0x23]);self.assertEqual([x[1]for x in self.s.calls if x[0]==0x21],list(range(8)))
 def test_fresh_token_single_use(self):
  self.setup();r=self.get();self.assertTrue(self.c.apply(dict(token=r['token'],rumble='low'))['ok']);n=len(self.writes())
  self.assertFalse(self.c.apply(dict(token=r['token'],rumble='high'))['ok']);self.assertEqual(len(self.writes()),n)
 def test_expired_no_config_writes(self):
  self.setup();r=self.get();self.now=3;self.assertFalse(self.c.apply(dict(token=r['token'],rumble='low'))['ok']);self.assertFalse(self.writes())
 def test_expiry_inside_last_native_getter_no_configwrite(self):
  self.setup();r=self.get();self.s.before=lambda code,page:setattr(self,'now',3) if code==0x21 else None
  x=self.c.apply(dict(token=r['token'],rumble='low'));self.assertFalse(x['ok']);self.assertFalse(self.writes());self.assertFalse(x['recoveryRequired'])
 def test_firmware_changes_in_write_ready_no_configuration(self):
  self.setup();r=self.get();original=self.s.exchange
  def changed(raw,**kwargs):
   result=original(raw,**kwargs)
   return result[:9]+bytes((2,))+result[10:] if raw[2]==0x20 else result
  self.s.exchange=changed
  result=self.c.apply(dict(token=r['token'],rumble='low'))
  self.assertFalse(result['ok']);self.assertEqual(result['reason'],'firmware_changed_before_write');self.assertFalse(result['recoveryRequired']);self.assertFalse(self.writes())
 def test_get_firmware_epoch_change_relinquishes_old_baseline(self):
  self.setup();self.assertTrue(self.apply('low')['ok']);self.s.xfirmware=(3,2)
  current=self.get();self.assertFalse(current['restoreAvailable']);self.assertFalse(self.c.restore(dict(token=current['token']))['ok'])
  self.assertTrue(self.apply('high')['ok']);self.assertTrue(self.c.restore(dict(token=self.get()['token']))['ok'])
  self.assertEqual(self.s.data[66],1)  # new epoch baseline low; never old firmware's off.
 def test_prewrite_firmware_epoch_change_relinquishes_without_write(self):
  self.setup();self.assertTrue(self.apply('low')['ok']);ticket=self.get();self.s.xfirmware=(3,2);n=len(self.writes())
  result=self.c.restore(dict(token=ticket['token']));self.assertFalse(result['ok']);self.assertEqual(result['reason'],'configuration_changed')
  self.assertEqual(len(self.writes()),n);self.assertFalse(self.get()['restoreAvailable'])
 def test_foreign_change_preserved_no_restore(self):
  self.setup();self.assertTrue(self.apply('low')['ok']);self.s.data=b'\xaa'+self.s.data[1:];foreign=self.s.data
  r=self.get();self.assertFalse(r['restoreAvailable']);x=self.c.restore(dict(token=r['token']));self.assertEqual(x['reason'],'nothing_to_restore');self.assertEqual(self.s.data,foreign)
 def test_foreign_after_ticket_no_write(self):
  self.setup();r=self.get();self.s.data=b'\xbb'+self.s.data[1:];x=self.c.apply(dict(token=r['token'],rumble='high'));self.assertFalse(x['ok']);self.assertFalse(self.writes())
 def test_new_baseline_after_foreign(self):
  self.setup();self.assertTrue(self.apply('low')['ok']);self.s.data=b'\xcc'+self.s.data[1:];baseline=self.s.data
  self.assertTrue(self.apply('high')['ok']);self.assertTrue(self.c.restore(dict(token=self.get()['token']))['ok']);self.assertEqual(self.s.data,baseline)
 def test_commit_lost_receipt_verified_not_retried(self):
  self.setup();self.s.fail=lambda code,page:'after'if code==0x23 else None;x=self.apply('low');self.assertTrue(x['ok'],x)
  self.assertEqual(sum(c==0x23 for c,_,_ in self.s.calls),1)
 def test_commit_before_failure_readback_old(self):
  self.setup();self.s.fail=lambda code,page:'before'if code==0x23 else None;x=self.apply('low');self.assertFalse(x['ok']);self.assertEqual(x['reason'],'write_not_applied');self.assertEqual(self.s.data,self.baseline);self.assertFalse(x['recoveryRequired'])
 def test_apply2_failure_keeps_original_baseline(self):
  self.setup();self.assertTrue(self.apply('low')['ok']);self.s.fail=lambda code,page:'before'if code==0x23 else None
  x=self.apply('high');self.assertFalse(x['ok']);self.s.fail=None
  self.assertTrue(self.c.restore(dict(token=self.get()['token']))['ok']);self.assertEqual(self.s.data,self.baseline)
 def test_staged_checksum_no_commit(self):
  self.setup();self.s.badchecksum=True;x=self.apply('high');self.assertFalse(x['ok']);self.assertFalse(any(c==0x23 for c,_,_ in self.s.calls));self.assertEqual(self.s.data,self.baseline)
 def test_partial_foreign_commit_unknown_no_blind_rollback(self):
  self.setup();self.s.fail=lambda code,page:'foreign'if code==0x23 else None;x=self.apply('low');self.assertFalse(x['ok']);self.assertTrue(x['recoveryRequired'])
  self.assertEqual(sum(c==0x23 for c,_,_ in self.s.calls),1);self.s.fail=None;self.s.data=self.baseline
  r=self.get();self.assertTrue(r['recoveryRequired']);self.assertFalse(r['writeAvailable']);self.assertFalse(self.c.apply(dict(token=r['token'],rumble='low'))['ok'])
 def test_unknown_fw_never_configwrite(self):
  self.setup(fw=(4,8));x=self.c.get();self.assertFalse(x['ok']);self.assertFalse(self.writes())
 def test_mini2024_exact_firmware(self):
  self.setup(('GPD','G1617-01'),(5,4));self.assertEqual(self.get()['profile'],'gpd-win-mini');self.assertTrue(self.apply('high')['verified'])
 def test_mini2025_denied_even_v1_vid(self):
  self.setup(('GPD','G1617-02'),(5,4));self.assertFalse(self.c.get()['ok']);self.assertFalse(self.writes())
 def test_desktop_denied(self):
  self.setup(('ASUS','DESKTOP'),(4,7));self.assertFalse(self.c.get()['ok']);self.assertFalse(self.writes())
 def test_short_report(self):
  self.setup();orig=self.s.exchange;self.s.exchange=lambda raw,**kw:orig(raw,**kw)[:-1]if kw['reply'] else orig(raw,**kw)
  self.assertFalse(self.c.get()['ok']);self.assertFalse(self.writes())
 def test_unstable_config_unavailable(self):
  self.setup();self.s.unstable=True;self.assertFalse(self.c.get()['ok']);self.assertFalse(self.writes())
 def test_wrong_checksum(self):
  self.setup();orig=self.s.exchange
  def wrong(raw,**kw):
   x=orig(raw,**kw)
   return x[:24]+b'\0\0\0\0'+x[28:]if raw[2]==0x12 else x
  self.s.exchange=wrong;self.assertFalse(self.c.get()['ok']);self.assertFalse(self.writes())
 def test_checksum_is_primary_uint16_with_unrelated_tail_preserved(self):
  self.setup();original=self.s.exchange
  def tail(raw,**kwargs):
   result=original(raw,**kwargs)
   return result[:26]+b'\xaa\xbb'+result[28:] if raw[2] in (0x12,0x22) else result
  self.s.exchange=tail
  self.assertTrue(self.apply('high')['verified'])
  self.assertEqual(self.s.data[66],2)
 def test_reconnect_no_old_baseline(self):
  self.setup();self.assertTrue(self.apply('low')['ok']);oldtoken=self.get()['token'];self.s.closed=True;self.assertFalse(self.c.get()['ok'])
  self.s=Session();self.t.s=self.s;r=self.get();self.assertNotEqual(r['token'],oldtoken);self.assertFalse(r['restoreAvailable'])
 def test_invalid_requests_no_configwrite(self):
  for value in(True,2,'max',None,{},[]):
   self.setup();r=self.get();self.assertFalse(self.c.apply(dict(token=r['token'],rumble=value))['ok']);self.assertFalse(self.writes())
 def test_extra_rgb_argument_not_mutated(self):
  self.setup();r=self.get();self.assertFalse(self.c.apply(dict(token=r['token'],rumble='low',rgb='ff0000'))['ok']);self.assertFalse(self.writes())
 def test_dispose_no_implicit_restore(self):
  self.setup();self.assertTrue(self.apply('low')['ok']);n=len(self.writes());r=self.c.dispose();self.assertTrue(r['hadOwnedBaseline']);self.assertFalse(r['restored']);self.assertEqual(len(self.writes()),n);self.assertFalse(self.c.get()['ok'])
 def test_timer_idle_releases_only_unowned(self):
  self.setup();self.get();self.now=3.1;self.c._timer.callback();self.assertTrue(self.s.closed)
  self.setup();self.assertTrue(self.apply('low')['ok']);self.assertIsNone(self.c._timer)
 def test_raw_command_whitelist(self):
  for code in (0,0x13,0x24,0xff):self.assertFalse(N.safe_command(M.command(code)))
  self.assertFalse(N.safe_command(M.command(0x11,2)));self.assertFalse(N.safe_command(M.command(0x21,8)))
  self.assertFalse(N.safe_command(M.command(0x23)+b'\0'))
 def test_primary_wire_vectors(self):
  # libOpenWinControls 63fd9062 ControllerV1::prepareSendPacket.
  self.assertEqual(M.command(0x10),bytes.fromhex('01a5105aef000000')+bytes(24))
  self.assertEqual(M.command(0x11,1),bytes.fromhex('01a5115aee000100')+bytes(24))
  self.assertEqual(M.command(0x21,7,bytes(range(16))),bytes.fromhex('01a5215ade000700')+bytes(range(16))+bytes(8))
if __name__=='__main__':unittest.main(verbosity=2)
