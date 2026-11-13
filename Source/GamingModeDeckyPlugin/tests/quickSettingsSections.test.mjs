import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

const compile = value => ts.transpileModule(value, { compilerOptions: {
  module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React,
} }).outputText;
const read = path => fs.readFileSync(new URL(path, import.meta.url), 'utf8');
const groups = {};
vm.runInNewContext(compile(read('../src/quickSettings/groups.ts')), { exports: groups });

const state = {};
vm.runInNewContext(compile(read('../src/controlCenterState.ts')), { exports: state });
const controlAst = ts.createSourceFile('ControlCenter.tsx', read('../src/ControlCenter.tsx'), ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const update = controlAst.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'update');
const control = controlAst.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'ControlCenter');
const updateExpanded = control.body.statements.find(node => ts.isVariableStatement(node)
  && node.declarationList.declarations.some(item => item.name.getText(controlAst) === 'updateExpanded'));
function sectionsFixture(saved = {}, fail = false) {
  let persisted = JSON.stringify(saved), saves = 0, warnings = 0;
  const React = { useCallback: fn => fn };
  const store = { initial: saved, React, normalizeControlPreferences: state.normalizeControlPreferences,
    controlLocale: () => ({ saveError: 'Could not save preferences' }),
    toaster: { toast: () => { warnings++; } },
    call: async (method, value) => {
      assert.equal(method, 'save_panel_preferences'); saves++;
      if (fail) throw new Error('Fixture storage unavailable');
      persisted = JSON.stringify(value);
    },
  };
  vm.createContext(store);
  vm.runInContext(compile(`let prefs = normalizeControlPreferences(initial), write = Promise.resolve();
    const listeners = new Set(), ready = true, locale = 'en';
    ${update.getText(controlAst)}
    function expand() { ${updateExpanded.getText(controlAst)} return updateExpanded; }
    function snapshot() { return prefs; }
    function settled() { return write; }`), store);
  const exports = {};
  vm.runInNewContext(compile(read('../src/useControlSections.ts')), {
    exports, require: name => name === './controlCenterState' ? state : ({ SP_REACT: React }),
  });
  return {
    render() { return exports.useControlSections(store.snapshot().expandedSections, store.expand()); },
    async settled() { await store.settled(); },
    saved() { return JSON.parse(persisted); },
    counts() { return { saves, warnings }; },
  };
}

test('each section retains its open or closed preference without changing device settings', async () => {
  const f = sectionsFixture();
  assert.deepEqual(Array.from(f.render().expanded), []);
  f.render().toggle('power');
  assert.deepEqual(Array.from(f.render().expanded), ['power']);
  f.render().toggle('radeon');
  assert.deepEqual(Array.from(f.render().expanded), ['power', 'radeon']);
  f.render().toggle('power');
  assert.deepEqual(Array.from(f.render().expanded), ['radeon']);
  await f.settled();
  assert.deepEqual(f.saved().expandedSections, ['radeon']);
  assert.deepEqual(f.saved().collapsed, []);
  assert.equal(f.counts().saves, 3);
});

test('tab changes, QAM reopening and a fresh plugin instance preserve the independent sections', async () => {
  const f = sectionsFixture();
  // One retained callback can receive several gamepad presses before React renders.
  const first = f.render(); first.toggle('power'); first.toggle('mixer'); first.toggle('display');
  await f.settled();
  const expected = ['display', 'mixer', 'power'];
  assert.deepEqual(Array.from(f.render('audio:false').expanded), expected);
  assert.deepEqual(Array.from(f.render('closed').expanded), expected);
  assert.deepEqual(Array.from(f.render('reopened').expanded), expected);
  const restarted = sectionsFixture(f.saved());
  assert.deepEqual(Array.from(restarted.render().expanded), expected);
  assert.deepEqual(restarted.counts(), { saves: 0, warnings: 0 });
  restarted.render().toggle('mixer'); await restarted.settled();
  assert.deepEqual(restarted.saved().expandedSections, ['display', 'power']);
});

test('nested device details survive collapsing their parent and stay independent of new sections', async () => {
  const f = sectionsFixture();
  f.render().toggle('device'); f.render().toggle('device/details'); f.render().toggle('device');
  await f.settled();
  const restarted = sectionsFixture(f.saved());
  assert.deepEqual(Array.from(restarted.render().expanded), ['device/details']);
  restarted.render().toggle('device');
  assert.deepEqual(Array.from(restarted.render().expanded), ['device', 'device/details']);
  assert.equal(restarted.render().expanded.includes('measurement'), false);
});

test('a storage failure leaves the panel usable and a later press updates the current in-memory state', async () => {
  const f = sectionsFixture({ expandedSections: ['display'] }, true);
  const first = f.render(); first.toggle('mixer'); first.toggle('power');
  await f.settled();
  assert.deepEqual(Array.from(f.render().expanded), ['display', 'mixer', 'power']);
  assert.deepEqual(f.saved().expandedSections, ['display']);
  assert.deepEqual(f.counts(), { saves: 2, warnings: 2 });
  f.render().toggle('display'); await f.settled();
  assert.deepEqual(Array.from(f.render().expanded), ['mixer', 'power']);
});

test('malformed saved expansion is closed, known IDs are deduplicated, and legacy collapsed state stays separate', () => {
  for (const value of [undefined, null, true, 'power', {}, 3])
    assert.deepEqual(Array.from(state.normalizeControlPreferences({ expandedSections: value }).expandedSections), []);
  const prefs = state.normalizeControlPreferences({ expandedSections: ['mixer', 'power', 'mixer', {}, '__proto__', 'news:new'], collapsed: ['radeon'] });
  assert.deepEqual(Array.from(prefs.expandedSections), ['mixer', 'power']);
  assert.deepEqual(Array.from(prefs.collapsed), ['radeon']);
  assert.equal(state.CONTROL_SECTION_IDS.length, 15);
  assert.deepEqual(Array.from(state.normalizeExpandedSections([...state.CONTROL_SECTION_IDS, ...state.CONTROL_SECTION_IDS])), Array.from(state.CONTROL_SECTION_IDS));
  assert.deepEqual(Array.from(state.normalizeExpandedSections([...Array(64).fill('unknown'), 'power'])), []);
});

const source = read('../src/quickSettings/index.tsx');
const ast = ts.createSourceFile('index.tsx', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const component = ast.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'QuickSettingsContent');
const section = component.body.statements.find(node => ts.isVariableStatement(node)
  && node.declarationList.declarations.some(item => item.name.getText(ast) === 'section'));
const surface = component.body.statements.find(node => ts.isReturnStatement(node));
const createElement = (type, props, ...children) => ({ type, props: props ?? {}, children });
const context = { React: { createElement, Fragment: 'Fragment' },
  ControlSection: 'ControlSection', Focusable: 'Focusable', DialogButton: 'DialogButton', ToggleField: 'ToggleField',
  PowerProfile: 'PowerProfile', ProcessorBoost: 'ProcessorBoost', HandheldPowerMode: 'HandheldPowerMode',
  MsiPowerControls: 'MsiPowerControls',
  HandheldPowerLimits: 'HandheldPowerLimits', OneXFanControls: 'OneXFanControls',
  LenovoPowerLimits: 'LenovoPowerLimits',
  AsusPerformanceActions: 'AsusPerformanceActions',
  AsusFanStatus: 'AsusFanStatus',
  GameExecutionProfile: 'GameExecutionProfile', ShaderCompatibility: 'ShaderCompatibility', GameAudioVolume: 'GameAudioVolume',
  QuickSlider: 'QuickSlider', QuickDropdown: 'QuickDropdown', PanelBrightness: 'PanelBrightness',
  PerformanceSettings: 'PerformanceSettings', SteamDownloadPower: 'SteamDownloadPower',
  quickSettingsGroups: groups.quickSettingsGroups, locale: 'en',
  local: new Proxy({}, { get: (_, key) => String(key) }),
  caps: { lossless: true, amd_radeon: true }, amd: { available: true }, lossless: { installed: true, can_scale: true },
  loadFailed: false, volume: { available: true, level: 50 }, audio: { ok: true, input_volume: 25, outputs: [], inputs: [] },
  hdr: {}, dimmer: { level: 0 }, display: null, agentRunning: false, diagnosticsBusy: false,
  outputOptions: [], inputOptions: [], resolutionOptions: [], refreshOptions: [], frameGenOptions: [],
  currentResolution: '', currentRefresh: '',
  dimmerToBrightness: value => 100 - value, clampPercent: value => value,
  softwareBrightnessLabel: () => 'Software brightness', controlLocale: () => ({}),
  setLoadFailed() {}, setLoadAttempt() {}, onToggle() {},
};
for (const name of source.match(/\b(?:Fa[A-Z]\w+|on[A-Z]\w+|queueAmdWrite)\b/g)) context[name] ??= () => {};
vm.createContext(context);
vm.runInContext(compile(`function render(page, expanded = [], gamingMode = false, gameAppId = 123) {
  const groups = quickSettingsGroups(locale); ${section.getText(ast)} ${surface.getText(ast)}
}`), context);
function nodes(value, found = []) {
  if (value && typeof value === 'object') { found.push(value); for (const child of value.children ?? []) nodes(child, found); }
  if (Array.isArray(value)) for (const child of value) nodes(child, found);
  return found;
}
test('Performance puts real power controls first and Lossless Scaling below AMD; every section is initially closed', () => {
  const tree = nodes(context.render('graphics', [], true));
  const sections = tree.filter(node => node.type === 'ControlSection');
  assert.deepEqual(sections.map(node => node.props.id), ['power', 'game', 'shaders', 'radeon', 'lossless']);
  assert.ok(sections.every(node => node.props.collapsed === true));
  const power = nodes(sections[0]);
  assert.ok(power.some(node => node.type === 'HandheldPowerMode'));
  assert.ok(power.some(node => node.type === 'MsiPowerControls'));
  assert.ok(power.some(node => node.type === 'AsusPerformanceActions'));
  assert.ok(power.some(node => node.type === 'AsusFanStatus'));
  assert.ok(power.some(node => node.type === 'HandheldPowerLimits'));
  assert.ok(power.some(node => node.type === 'OneXFanControls'));
  assert.ok(power.some(node => node.type === 'LenovoPowerLimits'));
  assert.ok(power.some(node => node.type === 'ProcessorBoost'));
});
test('Audio has a device/game mixer in both modes and a separate native device selector; Video contains no audio mixer', () => {
  for (const gaming of [false, true]) {
    const tree = nodes(context.render('performance', ['mixer'], gaming));
    assert.deepEqual(tree.filter(node => node.type === 'ControlSection').map(node => node.props.id), ['mixer', 'devices']);
    assert.ok(tree.some(node => node.type === 'GameAudioVolume' && node.props.appId === 123));
  }
  assert.ok(!nodes(context.render('audio')).some(node => node.type === 'GameAudioVolume'));
});
test('minimal group labels exist in all twelve supported languages', () => {
  for (const locale of ['en', 'it', 'de', 'es', 'fr', 'pt', 'ru', 'uk', 'ja', 'ko', 'zh', 'hi'])
    assert.ok(Object.values(groups.quickSettingsGroups(locale)).every(label => typeof label === 'string' && label.length));
});
