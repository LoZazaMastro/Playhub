import ast, asyncio, copy, pathlib, types, unittest
ROOT=pathlib.Path(__file__).resolve().parents[1]
source=ROOT/'main.py'
tree=ast.parse(source.read_text(encoding='utf-8-sig'))
method=next(n for n in ast.walk(tree) if isinstance(n,ast.AsyncFunctionDef) and n.name=='prepare_owned_mirror_presentation')
method.decorator_list=[]
space={'asyncio':asyncio};exec(compile(ast.Module(body=[method],type_ignores=[]),str(source),'exec'),space)
APP=3160580583
BASE={'canonicalShortcutAppId':APP,'originalGameId':str((APP<<32)|0x02000000),'observedSteamGameId':None,
 'gamePid':300,'gameBirth':'134389623572428537','wrapperPid':200,'wrapperBirth':'134389623500283156',
 'steamPid':100,'steamBirth':'134389561211987069','observedFresh':False,'routingOnly':True,'uiObservedApp':None,'launchProvenance':'epic-protocol'}
class Checks(unittest.IsolatedAsyncioTestCase):
 async def call(self,route):
  calls=[]
  def post(*args):calls.append(args);return {'ok':True,'mirror':{}}
  self.calls=calls
  obj=types.SimpleNamespace(_native_session_post=post)
  return await space[method.name](obj,APP,route)
 async def test_epic_null_preserved(self):
  self.assertTrue((await self.call(BASE))['ok']);body=self.calls[0][2]
  self.assertIsNone(body['expectedRoute']['observedSteamGameId']);self.assertEqual(body['expectedRoute']['gameBirth'],BASE['gameBirth'])
  self.assertEqual(body['expectedRoute']['launchProvenance'],'epic-protocol')
 async def test_null_requires_marker(self):
  r={**BASE,'launchProvenance':'steam-child'};self.assertFalse((await self.call(r))['ok']);self.assertEqual(self.calls,[])
 async def test_null_not_fresh(self):
  r={**BASE,'observedFresh':True};self.assertFalse((await self.call(r))['ok']);self.assertEqual(self.calls,[])
 async def test_actual_observation_kept(self):
  r={**BASE,'observedSteamGameId':'480','observedFresh':True,'launchProvenance':'steam-child'}
  self.assertTrue((await self.call(r))['ok']);self.assertEqual(self.calls[0][2]['expectedRoute']['observedSteamGameId'],'480')
 async def test_numeric_observation_rejected(self):
  self.assertFalse((await self.call({**BASE,'observedSteamGameId':480}))['ok']);self.assertEqual(self.calls,[])
 async def test_missing_is_not_null(self):
  r=copy.deepcopy(BASE);del r['observedSteamGameId'];self.assertFalse((await self.call(r))['ok']);self.assertEqual(self.calls,[])
 async def test_uint64_limit(self):
  self.assertFalse((await self.call({**BASE,'gameBirth':'18446744073709551616'}))['ok']);self.assertEqual(self.calls,[])
 async def test_unknown_marker_rejected(self):
  self.assertFalse((await self.call({**BASE,'launchProvenance':'other'}))['ok']);self.assertEqual(self.calls,[])
if __name__=='__main__':unittest.main()
