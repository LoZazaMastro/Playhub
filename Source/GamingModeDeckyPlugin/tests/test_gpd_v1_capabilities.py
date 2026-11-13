from pathlib import Path
import importlib.util,unittest
R=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('gpd_caps_actual',R/'quick_settings/device_capabilities.py')
cap=importlib.util.module_from_spec(spec);spec.loader.exec_module(cap)
def report(**changes):
 return dict(ok=True,available=True,provider='gpd-v1-hid',token='a'*64,profile='gpd-win-4',
  writeAvailable=True,recoveryRequired=False,state={'rumble':'low'},firmware={'x':[1,2],'k':[4,7]},
  readbackKind='gpd-v1-config128-checksum-double-read',**changes)
def ident(board='G1618-04'):return dict(manufacturer='GPD',model=board,board_manufacturer='GPD',board_model=board)
def profile(effective='gpd-win-4',detected=None):return dict(selection='auto',effective=effective,detected=detected or effective,power={},display=None)
class Tests(unittest.TestCase):
 def test_capabilities_supported_real_report(self):
  x=cap._control('vibration','gpd-v1',report(),ident(),'gpd-win-4');self.assertTrue(x['available']);self.assertEqual(x['destination'],'device');self.assertEqual(x['status'],'readback')
 def test_capabilities_unknown_and_wrongtyped_refused(self):
  for patch in [{'firmware':{'k':[4,8]}},{'firmware':{'k':[True,7]}},{'profile':'gpd-win-mini'},{'token':'bad'},{'state':{'rumble':'medium'}},{'provider':'ayaneo3-hid'},{'recoveryRequired':True}]:
   x=report();x.update(patch);self.assertFalse(cap._control('vibration','gpd-v1',x,ident(),'gpd-win-4')['available'])
 def test_capabilities_uncertain_readonly(self):
  x=report();x.update(writeAvailable=False,recoveryRequired=True)
  self.assertEqual(cap._control('vibration','gpd-v1',x,ident(),'gpd-win-4')['status'],'read-only')
 def test_capabilities_no_provider_on_manual_pc(self):
  calls=[];m=cap.DeviceCapabilities(lambda:profile(detected='pc'),lambda:ident(),{'gpd-v1':(cap.gpd_v1_identity,lambda:calls.append(1))})
  try:x=m.get();self.assertFalse(calls);self.assertEqual(x['controls'][-1]['status'],'hardware-required')
  finally:m.close()
 def test_mini2025_presentation_does_not_probe(self):
  calls=[];m=cap.DeviceCapabilities(lambda:profile('gpd-win-mini'),lambda:ident('G1617-02'),{'gpd-v1':(cap.gpd_v1_identity,lambda:calls.append(1))})
  try:x=m.get();self.assertFalse(calls);self.assertFalse(x['controls'][-1]['available'])
  finally:m.close()
 def test_board_required_no_model_only_grant(self):
  for identity in ({'manufacturer':'GPD','model':'G1618-04'},{'manufacturer':True,'board_model':'G1618-04'},{'manufacturer':'GPD','board_model':True},ident('G1618-05')):
   self.assertFalse(cap.gpd_v1_identity(identity))
 def test_provider_current_report_via_actual_queue(self):
  calls=[]
  def get():calls.append(1);return report()
  m=cap.DeviceCapabilities(lambda:profile(),lambda:ident(),{'gpd-v1':(cap.gpd_v1_identity,get)})
  try:x=m.get();self.assertTrue(x['controls'][-1]['available']);self.assertEqual(calls,[1])
  finally:m.close()

if __name__=='__main__':unittest.main(verbosity=2)
