import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';
import ts from 'typescript';

function load(name, dependency) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(readFileSync(new URL(`../src/${name}.ts`, import.meta.url), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, require: () => dependency });
  return exports;
}
const control = load('controlledQamFocus');
const browserApi = load('dashboardOverlay', control);
const { createSteamMirrorOverlay } = load('steamMirrorOverlay', browserApi);
const app = 2485417537, gameId = ((BigInt(app) << 32n) | 0x02000000n).toString();
function fixture(options = {}) {
  let clock = 1000, side = 0, session = 0, generation = 0, native, firstRead, firstDiscovery;
  const calls = [], events = [];
  const identities = [
    { sessionId: 'a'.repeat(64), overlayPid: 31704, ownerPid: 27156, ownerBirth: '134389155136169895', gamePid: 48584, gameBirth: '134389155148953005' },
    { sessionId: 'b'.repeat(64), overlayPid: options.reusePids ? 31704 : 31705, ownerPid: options.reusePids ? 27156 : 27157,
      ownerBirth: '134389265873507919', gamePid: options.reusePids ? 48584 : 8052, gameBirth: '134389265877510461' },
  ];
  const menu = { GetOpenSideMenu: () => side, GetQuickAccessTab: () => 0,
    OpenMainMenu() { side = 1; }, OpenQuickAccessMenu() { side = 2; } };
  const target = { IsGamepadUIOverlayWindow: () => true, MenuStore: menu, BrowserWindow: { document: { body: {} } },
    get params() { return { browserInfo: { m_gameID: gameId, m_unPID: identities[session].overlayPid, m_nBrowserID: 0 } }; } };
  const reply = () => ({ ok: true, gameId, companionPid: identities[session].ownerPid,
    overlayPid: identities[session].overlayPid, mirrorSessionId: identities[session].sessionId, mirror: { ...native } });
  const discovery = () => ({ ok: false, error: 'mirror-not-prepared', gameId,
    companionPid: identities[session].ownerPid, overlayPid: identities[session].overlayPid,
    mirrorSessionId: identities[session].sessionId });
  const route = createSteamMirrorOverlay({
    runningApp: () => app, now: () => clock, trace: value => events.push(value),
    prepare: async () => {
      calls.push(['prepare', session]); generation++;
      native = { protocol: 1, ...identities[session], generation: String(generation), gameId,
        state: 'prepared', expiresAt: '90000', observedAt: '1000', sourceHwnd: '41001', sourceCookie: '18446744073709551614',
        destinationHwnd: '41002', destinationCookie: '18446744073709551615', preparedCurrent: true, shownCurrent: false,
        leaseCurrent: true, cleanupComplete: false, actualScope: 1 };
      return reply();
    },
    read: async (_app, pid) => {
      assert.equal(_app, app); calls.push(['read', session, pid]);
      if (firstRead) { const value = firstRead; firstRead = undefined; return value(); }
      if (pid === 0) {
        if (firstDiscovery) { const value = firstDiscovery; firstDiscovery = undefined; await value(); }
        return options.discovery ? options.discovery(discovery()) : native ? reply() : discovery();
      }
      if (pid !== identities[session].overlayPid) return { ok: false };
      return native ? reply() : discovery();
    },
    command: async (_app, pid, action, expected) => {
      assert.equal(_app, app);
      if (!native || pid !== identities[session].overlayPid || expected.sessionId !== native.sessionId
          || expected.generation !== native.generation) { calls.push(['refused', action]); return { ok: false }; }
      calls.push([action, session]);
      native = action === 'show' ? { ...native, state: 'shown', shownCurrent: true }
        : { ...native, state: 'closed', leaseCurrent: false, preparedCurrent: false, shownCurrent: false,
          cleanupComplete: true, destinationHwnd: '0', destinationCookie: '0' };
      return { ok: true };
    },
    browserInfo: async () => [{ appID: app, gameID: gameId, unPID: identities[session].overlayPid, nBrowserID: 0 }],
    windows: () => [target], setOverlayState: async (_id, state) => { assert.equal(_id, gameId); calls.push([`set${state}`, session]); },
    delay: async ms => { clock += ms; },
  });
  return { route, calls, events, reply, discovery, switchSession() { session = 1; generation = 0; native = undefined; side = 0; },
    deferRead(value) { firstRead = value; }, deferDiscovery(value) { firstDiscovery = value; } };
}

test('same app in a new session reopens on the first Guide, without old Close or Set0', async () => {
  for (const menu of ['main', 'quick']) {
    const f = fixture(); await f.route.guide(menu); f.switchSession(); const start = f.calls.length;
    await f.route.guide(menu);
    assert.equal(f.route.shown, true);
    assert.equal(f.calls.filter(x => x[0] === 'prepare').length, 2);
    assert.equal(f.calls.slice(start).some(x => x[0] === 'close' || x[0] === 'set0'), false);
    assert.equal(f.events.includes('mirror-session-replaced'), true);
    await f.route.dispose();
  }
});
test('same numeric PIDs with different retained births still require and use a different session hash', async () => {
  const f = fixture({ reusePids: true }); await f.route.guide('main'); f.switchSession();
  await f.route.guide('main'); assert.equal(f.route.shown, true);
  assert.equal(f.calls.filter(x => x[0] === 'prepare').length, 2);
  await f.route.dispose();
});
test('application invalidation can retire only the old local receipt when its session was replaced', async () => {
  const f = fixture(); await f.route.guide('main'); f.switchSession(); const start = f.calls.length;
  await f.route.invalidate(); assert.equal(f.route.active, false);
  assert.equal(f.calls.slice(start).some(x => x[0] === 'close' || x[0] === 'set0'), false);
  await f.route.guide('quick'); assert.equal(f.route.shown, true); await f.route.dispose();
});
test('an explicit close after session replacement never prepares or closes the replacement', async () => {
  const f = fixture(); await f.route.guide('main'); f.switchSession(); const start = f.calls.length;
  await f.route.close(); assert.equal(f.route.active, false);
  assert.equal(f.calls.slice(start).some(x => ['prepare', 'close', 'set0'].includes(x[0])), false);
});
test('unknown, same-session or malformed discovery cannot abandon the uncertain old owner', async () => {
  const variants = [() => ({ ok: false }), v => ({ ...v, mirrorSessionId: undefined }),
    v => ({ ...v, mirrorSessionId: 'a'.repeat(64) }), v => ({ ...v, mirrorSessionId: 'b'.repeat(63) }),
    v => ({ ...v, companionPid: 0 }), v => ({ ...v, overlayPid: '31705' }), v => ({ ...v, gameId: '99' }),
    v => ({ ...v, error: 'mirror-unavailable' }), v => ({ ...v, ok: true, mirror: { sessionId: 'a'.repeat(64) } })];
  for (const discovery of variants) {
    const f = fixture({ discovery }); await f.route.guide('main'); f.switchSession(); const start = f.calls.length;
    await f.route.guide('main'); assert.equal(f.route.active, true);
    assert.equal(f.calls.filter(x => x[0] === 'prepare').length, 1);
    assert.equal(f.calls.slice(start).some(x => x[0] === 'close' || x[0] === 'set0'), false);
  }
});
test('cancellation while discovery is pending cannot reopen the newly discovered session', async () => {
  const f = fixture(); await f.route.guide('main'); f.switchSession();
  let release, entered; const gate = new Promise(resolve => { release = resolve; });
  const seen = new Promise(resolve => { entered = resolve; });
  f.deferDiscovery(() => { entered(); return gate; });
  const open = f.route.guide('main'); await seen;
  const close = f.route.close(); release(); await Promise.all([open, close]);
  assert.equal(f.calls.filter(x => x[0] === 'prepare').length, 1);
  assert.equal(f.route.active, false);
  assert.equal(f.calls.some(x => x[0] === 'set0'), false);
});
test('a delayed terminal reply from an earlier close cannot Set0 after a newer Guide revision', async () => {
  const f = fixture(); await f.route.guide('main');
  const old = f.reply();
  const terminal = { ...old, mirror: { ...old.mirror, state: 'closed', leaseCurrent: false, preparedCurrent: false,
    shownCurrent: false, cleanupComplete: true, destinationHwnd: '0', destinationCookie: '0' } };
  let release, entered; const gate = new Promise(resolve => { release = resolve; });
  const seen = new Promise(resolve => { entered = resolve; });
  f.deferRead(async () => { entered(); await gate; return terminal; });
  const close = f.route.close(); await seen; f.switchSession(); const open = f.route.guide('main');
  release(); await Promise.all([close, open]);
  assert.equal(f.calls.some(x => x[0] === 'set0'), false);
  assert.equal(f.route.shown, true); await f.route.dispose();
});
test('a normal same-session close uses its retained read and does not perform discovery', async () => {
  const f = fixture(); await f.route.guide('main'); const start = f.calls.length;
  await f.route.guide('main'); assert.equal(f.route.active, false);
  assert.equal(f.calls.slice(start).some(x => x[0] === 'read' && x[2] === 0), false);
  assert.equal(f.calls.some(x => x[0] === 'set0'), true);
});
