import ast,asyncio,unittest,json
from pathlib import Path
from unittest.mock import Mock
root=Path(__file__).parents[1]
main=root/'main.py'
if not main.exists():main=root/'runtime/main.py'
tree=ast.parse(main.read_text(encoding='utf-8-sig'))
cls=next(x for x in tree.body if isinstance(x,ast.ClassDef) and x.name=='Plugin')
names={'_native_game_identity','get_native_game_overlay','set_native_game_overlay_input'}
selected=[x for x in cls.body if isinstance(x,(ast.FunctionDef,ast.AsyncFunctionDef)) and x.name in names]
module=ast.Module(body=[ast.ClassDef(name='Plugin',bases=[],keywords=[],body=selected,decorator_list=[])],type_ignores=[])
ns={'asyncio':asyncio};exec(compile(ast.fix_missing_locations(module),'actual-main.py','exec'),ns);Plugin=ns['Plugin']
app=2979426017
proof=dict(protocol=1,canonicalAppId=app,fullGameId='12796537303900094464',observedGameId='2979426017',sessionId='a'*64,gamePid=10,gameBirth='134389511886362822',wrapperPid=11,wrapperBirth='134389511880000001',steamPid=12,steamBirth='134388877300000001',overlayPid=14,overlayBirth='134389511900000001',companionPid=13,companionBirth='134389511890000001',gameWindow='291',observedAt='100',expiresAt='1600',routingOnly=True)
browser=dict(overlayPid=14,browserId=17,appId=9858785)
class Rpc(unittest.TestCase):
 def setUp(self):self.p=Plugin();self.p._native_session_post=Mock(return_value={'ok':True,'kind':'native-game','nativeGame':proof})
 def test_read(self):
  self.assertTrue(asyncio.run(self.p.get_native_game_overlay(app))['ok']);self.p._native_session_post.assert_called_once_with('/session/steam/native-game-overlay',None,{'appId':app})
 def test_known_classification(self):
  for kind in ('packaged','absent'):
   self.p._native_session_post.return_value={'ok':True,'kind':kind};self.assertEqual(asyncio.run(self.p.get_native_game_overlay(app)),{'ok':True,'kind':kind})
 def test_unknown_not_absence(self):
  self.p._native_session_post.return_value={'ok':False,'kind':'absent'};self.assertFalse(asyncio.run(self.p.get_native_game_overlay(app))['ok'])
 def test_incomplete_proof(self):
  self.p._native_session_post.return_value={'ok':True,'kind':'native-game','nativeGame':{'protocol':1}};self.assertFalse(asyncio.run(self.p.get_native_game_overlay(app))['ok'])
 def test_bad_app(self):
  for value in (True,0,480,2**32):self.assertFalse(asyncio.run(self.p.get_native_game_overlay(value))['ok'])
  self.p._native_session_post.assert_not_called()
 def test_prepare(self):
  asyncio.run(self.p.set_native_game_overlay_input(app,'prepare',{**proof,'shown':True,'actualScope':2},browser))
  args,kwargs=self.p._native_session_post.call_args;self.assertEqual(args[:2],('/session/steam/native-game-overlay-input',None));self.assertEqual(args[2]['expectedNative'],proof);self.assertEqual(kwargs,dict(timeout=7.0,maximum=8192))
 def test_lossless_strings(self):
  asyncio.run(self.p.set_native_game_overlay_input(app,'prepare',proof,browser));body=self.p._native_session_post.call_args.args[2];self.assertIn('"gameBirth": "134389511886362822"',json.dumps(body))
 def test_malformed_birth(self):
  for value in (134389511886362822,'Ù¡Ù¢Ù£','1e17','0123','18446744073709551616'):
   self.assertFalse(asyncio.run(self.p.set_native_game_overlay_input(app,'prepare',{**proof,'gameBirth':value},browser))['ok'])
  self.p._native_session_post.assert_not_called()
 def test_callbacks(self):
  token='b'*32
  for action,active in [('active',True),('renew',True),('closed',False)]:
   self.p._native_session_post.reset_mock();c={**browser,'active':active,'serial':'134389511886362822'};asyncio.run(self.p.set_native_game_overlay_input(app,action,proof,browser,token,'18446744073709551615',c));self.assertEqual(self.p._native_session_post.call_args.args[2]['callback'],c)
 def test_wrong_callback(self):
  for field,value in [('overlayPid',15),('browserId',18),('appId',2979426017),('active',False),('serial',2),('serial','0')]:
   c={**browser,'active':True,'serial':'1',field:value};self.assertFalse(asyncio.run(self.p.set_native_game_overlay_input(app,'active',proof,browser,'b'*32,'1',c))['ok'])
  self.p._native_session_post.assert_not_called()
 def test_positive_uint32_native_app_not_truncated(self):
  asyncio.run(self.p.set_native_game_overlay_input(app,'prepare',proof,{**browser,'appId':2979426017}));self.assertEqual(self.p._native_session_post.call_args.args[2]['browser']['appId'],2979426017)
 def test_failure_receipt_and_ordinary_preserved(self):
  response={'ok':False,'ownerToken':'b'*32,'generation':'1','inputMode':'ordinary','ordinaryReady':True,'noOwnedOverride':True};self.p._native_session_post.return_value=response;self.assertEqual(asyncio.run(self.p.set_native_game_overlay_input(app,'prepare',proof,browser)),response)
 def test_abort_exact_owned_cleanup_transport(self):
  response={'ok':True,'aborted':True,'closed':False,'cleanupComplete':True,'ownerToken':'b'*32,'generation':'1','nativeGame':proof}
  self.p._native_session_post.return_value=response
  self.assertEqual(asyncio.run(self.p.set_native_game_overlay_input(app,'abort',proof,browser,'b'*32,'1')),response)
  body=self.p._native_session_post.call_args.args[2]
  self.assertEqual(body['action'],'abort');self.assertEqual(body['ownerToken'],'b'*32);self.assertEqual(body['generation'],'1');self.assertNotIn('callback',body)
 def test_abort_requires_owned_token_and_generation(self):
  for token,generation in [(None,None),('bad','1'),('b'*32,'0')]:
   self.assertFalse(asyncio.run(self.p.set_native_game_overlay_input(app,'abort',proof,browser,token,generation))['ok'])
  self.p._native_session_post.assert_not_called()
if __name__=='__main__':unittest.main()
